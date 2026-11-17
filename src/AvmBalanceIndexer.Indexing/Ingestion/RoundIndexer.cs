using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>Outcome of one <see cref="RoundIndexer.RunAsync"/> call.</summary>
public sealed record IndexingResult(ulong FirstRound, ulong LastRound, long RoundsIndexed, long EffectsWritten);

/// <summary>
/// Indexes one chain in round order: bootstraps genesis balances as round 0, then fetches blocks with bounded
/// parallelism, derives effects, and commits each round through the storage provider. The published cursor
/// (round and block hash) advances only as part of the round commit, and each block must link to the previous one.
/// </summary>
public sealed partial class RoundIndexer(
    IChainAdapter adapter,
    IEffectNormalizer normalizer,
    IChainStorage storage,
    IOptions<IndexerOptions> options,
    ILogger<RoundIndexer> logger,
    TimeProvider? timeProvider = null,
    IndexingMetrics? metrics = null,
    IArc200MetadataSource? arc200Metadata = null)
{
    private readonly IndexerOptions _options = options.Value;
    private readonly IndexingMetrics _metrics = metrics ?? IndexingMetrics.None;

    /// <summary>Malformed ARC-200 logs of the queued, not yet published rounds: counted when they are published, so a replay does not count twice.</summary>
    private int _pendingMalformedArc200Logs;
    private readonly HashSet<ulong> _knownArc200 = [];

    /// <summary>ARC-200 tokens whose record is written but whose metadata is still to be read, with the time of the next attempt.</summary>
    private readonly Dictionary<ulong, DateTimeOffset> _arc200Pending = [];

    /// <summary>How often a token answered none of the four methods; after <see cref="MaxEmptyAnswers"/> it is left alone (not an ARC-200 token, or no metadata).</summary>
    private readonly Dictionary<ulong, int> _arc200EmptyAnswers = [];
    private DateTimeOffset _arc200LookupsPausedUntil;
    private const int MaxEmptyAnswers = 3;
    private static readonly TimeSpan MetadataLookupTimeout = TimeSpan.FromSeconds(10);
    private const int MetadataLookupsPerBlock = 2;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private static readonly TimeSpan FlushGracePeriod = TimeSpan.FromSeconds(10);
    private long _roundsCommitted;
    private bool _writeFailed;
    private ulong _lastLatest;
    private long? _latestAdvancedAt; // monotonic timestamp (TimeProvider.GetTimestamp), unaffected by clock steps
    private bool _tipStale;

    /// <summary>Total rounds this instance has committed; lets a supervisor tell progress from a stall without reading storage.</summary>
    public long RoundsCommitted => Interlocked.Read(ref _roundsCommitted);

    /// <summary>The hash of the block of a round, fetched like any block of the chain (a partial rebuild is checked against it).</summary>
    /// <param name="round">The round.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    public async Task<byte[]> GetBlockHashAsync(ulong round, CancellationToken cancellationToken) =>
        (await adapter.FetchBlockAsync(round, cancellationToken).ConfigureAwait(false)).Hash;

    /// <summary>
    /// Forgets what this instance learned from the database before it was changed underneath it (a partial rebuild moved it back to an
    /// earlier round): the ARC-200 applications it has seen, and the metadata lookups under way. The balances are read per run.
    /// </summary>
    public void Reset()
    {
        _knownArc200.Clear();
        _arc200Pending.Clear();
        _arc200EmptyAnswers.Clear();
        _arc200LookupsPausedUntil = default;
    }

    /// <summary>
    /// Indexes from the stored cursor onwards.
    /// </summary>
    /// <param name="stopAfterRound">
    /// Last round to index. When null the indexer follows the chain tip until cancelled.
    /// </param>
    /// <param name="cancellationToken">Stops indexing; the last committed round stays published.</param>
    /// <exception cref="ChainDivergenceException">A block does not link to the published chain.</exception>
    public async Task<IndexingResult> RunAsync(ulong? stopAfterRound, CancellationToken cancellationToken)
    {
        _writeFailed = false;
        _lastLatest = 0;
        _latestAdvancedAt = null;
        _tipStale = false;
        _pendingMalformedArc200Logs = 0;
        var chain = adapter.Profile.Chain;
        var status = await storage.GetAsync(chain, cancellationToken).ConfigureAwait(false);
        RewardsState rewards;
        while (true)
        {
            try
            {
                if (status is null)
                {
                    (status, rewards) = await BootstrapGenesisAsync(stopAfterRound, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    rewards = await SeedRewardsAsync(status, stopAfterRound, cancellationToken).ConfigureAwait(false);
                }

                break;
            }
            catch (BlockNotAvailableException ex) when (stopAfterRound is null)
            {
                // A very young network or a fleet that lags the tip: block 1 or the cursor block is not served yet. If algod
                // is well ahead, the archive is missing the block for good and the supervisor must hear about it.
                await WaitIfJustMissingAsync(ex, cancellationToken).ConfigureAwait(false);
            }
        }

        var tracker = new BalanceTracker(storage, _options.BalanceCacheEntries);
        var next = status.IndexedRound + 1;
        var first = next;
        long rounds = 0, effects = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A bounded run knows its target and does not need algod at all.
            var latest = stopAfterRound ?? await adapter.GetLatestRoundAsync(cancellationToken).ConfigureAwait(false);
            var target = latest;
            if (stopAfterRound is null)
            {
                NoteLatest(latest, status);
            }

            if (next > target)
            {
                if (stopAfterRound is not null)
                {
                    break;
                }

                if (!TipIsStale(latest))
                {
                    status = await HeartbeatAsync(status, latest, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(_options.FollowPollInterval, _time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var pending = new List<RoundWrite>();
            var pendingEffects = 0;
            var hinted = Hinted(stopAfterRound, next, target);
            try
            {
                await foreach (var fetched in OrderedPrefetch.StreamAsync(
                    next, target, _options.FetchConcurrency, FetchFor(hinted, target), cancellationToken).ConfigureAwait(false))
                {
                    var previous = status;
                    var block = hinted ? await ArchivalIfForeignAsync(fetched, b => LinksTo(previous, b), cancellationToken).ConfigureAwait(false) : fetched;
                    var queued = await QueueBlockAsync(chain, tracker, status, rewards, block, target, cancellationToken).ConfigureAwait(false);
                    status = queued.Status;
                    pending.Add(queued);
                    pendingEffects += queued.Effects.Count;
                    rewards = RewardsState.FromHeader(block.Block);
                    next = block.Round + 1;
                    rounds++;
                    effects += queued.Effects.Count;

                    // Batching is for backfills; near the tip (or at the end of a bounded run) every round is published at once.
                    var backfilling = target - block.Round > (ulong)_options.FollowingThresholdRounds;
                    if (!backfilling || pending.Count >= _options.CommitBatchRounds || pendingEffects >= _options.CommitBatchEffects || block.Round == target)
                    {
                        await FlushAsync(tracker, pending, cancellationToken).ConfigureAwait(false);
                        pendingEffects = 0;
                    }

                    if (rounds % _options.LogEveryRounds == 0)
                    {
                        LogProgress(logger, chain, block.Round, target);
                    }
                }
            }
            catch (Exception ex) when (pending.Count > 0 && !_writeFailed && ex is not (OperationCanceledException or ChainDivergenceException) && !(ex is BlockNotAvailableException && stopAfterRound is null))
            {
                // A failing fetch or a bad block must not throw away the rounds already computed: with batches of 25, a flaky host
                // would otherwise keep a backfill from ever completing one. Best effort within a grace period. Cancellation is
                // excluded on purpose: it also means a lost writer lease, after which this process must not write again.
                await TryFlushAsync(tracker, pending).ConfigureAwait(false);
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
            catch (BlockNotAvailableException ex) when (stopAfterRound is null)
            {
                // What was computed so far is written first, so the wait does not hold rounds back.
                await FlushAsync(tracker, pending, cancellationToken).ConfigureAwait(false);

                // At the tip the archival nodes may not have the newest block yet; wait and ask again. A block far behind the
                // tip that nobody serves is a stall, not a wait.
                // No heartbeat here on purpose: at the tip a block comes every few seconds, so a writer that cannot get one for
                // longer than the stall threshold is stalled, and the status must show it (#168).
                await WaitIfJustMissingAsync(ex, cancellationToken).ConfigureAwait(false);
            }

            await FlushAsync(tracker, pending, cancellationToken).ConfigureAwait(false);
        }

        return new IndexingResult(first, status.IndexedRound, rounds, effects);
    }

    /// <summary>
    /// The fetch for one pass. A pass that starts near the tip is short, its target is still the chain's latest round and tells the
    /// fetcher which blocks relays may hold. A bounded run's target can be far behind the tip, and a backfill pass lasts so long
    /// that the tip leaves its target behind, where relays would only answer 404: both give no hint.
    /// </summary>
    private bool Hinted(ulong? stopAfterRound, ulong first, ulong target) =>
        stopAfterRound is null && target - first <= (ulong)_options.FollowingThresholdRounds;

    /// <summary>The fetch of a pass: with the tip as hint when <paramref name="hinted"/> (see <see cref="Hinted"/>), without otherwise.</summary>
    private Func<ulong, CancellationToken, Task<BlockData>> FetchFor(bool hinted, ulong target) =>
        hinted ? (round, token) => adapter.FetchBlockAsync(round, target, token) : adapter.FetchBlockAsync;

    /// <summary>
    /// A block fetched with the tip hint may come from a public relay, and certificate votes are not checked (ADR 0004): one that does
    /// not link to the previous round is fetched again from the archival nodes alone (without the hint), whose answer decides a
    /// divergence. When theirs links, the fetcher learns that the first one was wrong (it rests and reports the relay). A block that
    /// did not come from a relay is not asked for again: the same nodes would answer the same.
    /// </summary>
    private async Task<BlockData> ArchivalIfForeignAsync(BlockData hinted, Func<BlockData, bool> belongs, CancellationToken cancellationToken)
    {
        if (hinted.Relay is null || belongs(hinted))
        {
            return hinted;
        }

        var archival = await adapter.FetchBlockAsync(hinted.Round, cancellationToken).ConfigureAwait(false);
        if (belongs(archival))
        {
            adapter.ReportWrongBlock(hinted);
        }

        return archival;
    }

    private static bool LinksTo(ChainStatus previous, BlockData block) =>
        block.PreviousHash.AsSpan().SequenceEqual(previous.IndexedBlockHash.Span);

    /// <summary>Remembers when algod's latest round last advanced (any change of the answer counts, also a lower one from another node behind a balancer; a balancer that alternates a stuck and a moving node cannot be told apart).</summary>
    private void NoteLatest(ulong latest, ChainStatus published)
    {
        if (_latestAdvancedAt is null)
        {
            // The first answer of this run: a restart or a failover must not make a stuck tip look fresh again. When the published
            // status already names this round as the target, the tip has been there at least since that status was written.
            _latestAdvancedAt = _time.GetTimestamp();
            if (published.TargetRound == latest && published.State == IngestionState.Following)
            {
                var age = _time.GetUtcNow() - published.UpdatedAt;
                if (age > TimeSpan.Zero)
                {
                    _latestAdvancedAt -= (long)(age.TotalSeconds * _time.TimestampFrequency);
                }
            }

            _lastLatest = latest;
        }
        else if (latest != _lastLatest)
        {
            // Any change counts: a node behind a balancer or a rebuilt one may answer lower than the old maximum and still progress.
            _lastLatest = latest;
            _latestAdvancedAt = _time.GetTimestamp();
            if (_tipStale)
            {
                _tipStale = false;
                LogTipAdvancing(logger, latest);
            }
        }
    }

    /// <summary>
    /// True while algod's latest round has not advanced for <see cref="IndexerOptions.TipStaleAfter"/>: the chain or the node is stuck and
    /// the idle writer must not claim to be healthy. Logs once when it starts and once when it ends.
    /// </summary>
    private bool TipIsStale(ulong latest)
    {
        var quiet = _time.GetElapsedTime(_latestAdvancedAt!.Value);
        var stale = _options.TipStaleAfter > TimeSpan.Zero && quiet > _options.TipStaleAfter;
        if (stale && !_tipStale)
        {
            LogTipStale(logger, latest, _time.GetUtcNow() - quiet);
        }

        _tipStale = stale;
        return stale;
    }

    /// <summary>
    /// A writer that has caught up with the tip is alive: it republishes the status with the latest round and a fresh timestamp, so that
    /// it is distinguishable from a stalled one (the API reports an old status as stalled).
    /// </summary>
    private async Task<ChainStatus> HeartbeatAsync(ChainStatus status, ulong latest, CancellationToken cancellationToken)
    {
        status = status with { TargetRound = latest, State = IngestionState.Following, UpdatedAt = _time.GetUtcNow() };
        await storage.UpdateProgressAsync(status, cancellationToken).ConfigureAwait(false);
        return status;
    }

    /// <summary>
    /// Waits one poll interval when the missing block is within propagation distance of the chain tip; otherwise rethrows so
    /// that the worker backs off, logs and eventually stops instead of polling a hole in the archive forever.
    /// </summary>
    private async Task WaitIfJustMissingAsync(BlockNotAvailableException missing, CancellationToken cancellationToken)
    {
        var latest = await adapter.GetLatestRoundAsync(cancellationToken).ConfigureAwait(false);
        if (missing.Round + (ulong)_options.FollowingThresholdRounds <= latest)
        {
            ExceptionDispatchInfo.Capture(missing).Throw();
        }

        LogWaitingForBlock(logger, missing.Round);
        await Task.Delay(_options.FollowPollInterval, _time, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(ChainStatus Status, RewardsState Rewards)> BootstrapGenesisAsync(ulong? stopAfterRound, CancellationToken cancellationToken)
    {
        var profile = adapter.Profile;
        var genesis = await adapter.GetGenesisAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(genesis.Id, profile.GenesisId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Algod serves genesis \"{genesis.Id}\" but chain {profile.Chain} expects \"{profile.GenesisId}\".");
        }

        // The round 0 balances come from this document, the blocks from the archival nodes. Requiring the document to hash to
        // the pinned genesis hash (which every block header carries) ties the two sources to the same chain.
        if (!genesis.Hash.AsSpan().SequenceEqual(adapter.GenesisHash.Span))
        {
            throw new InvalidDataException(
                $"The genesis served by algod does not hash to the genesis hash pinned for chain {profile.Chain}. Check Algorand:AlgodUrl.");
        }

        // Block 1 links back to the genesis block hash, which becomes the round 0 cursor hash.
        var firstBlock = await adapter.FetchBlockAsync(1, cancellationToken).ConfigureAwait(false);

        var effects = genesis.Accounts
            .Where(static a => a.Balance > 0)
            .Select((account, ordinal) => new TokenEffect(
                new EffectId(0, 0, string.Empty, [], ordinal),
                account.Address,
                TokenKey.Native,
                EffectType.Genesis,
                account.Balance,
                Amount.From(account.Balance),
                null))
            .ToArray();

        await storage.UpsertAsync(
            new TokenInfo(TokenKey.Native, null, 6, profile.NativeSymbol, profile.NativeName, TokenStandard.Native, true, null, 0, null, null),
            cancellationToken).ConfigureAwait(false);

        var status = new ChainStatus(profile.Chain, 0, firstBlock.PreviousHash, stopAfterRound, IngestionState.Bootstrapping, _time.GetUtcNow());
        if (!await storage.WriteRoundAsync(status, effects, cancellationToken).ConfigureAwait(false))
        {
            throw new ChainDivergenceException($"Genesis of {profile.Chain} was published by another writer. Only one indexer may write a chain.");
        }

        LogGenesis(logger, profile.Chain, effects.Length);
        return (status, GenesisRewardsState(firstBlock));
    }

    /// <summary>
    /// The genesis block starts with rewards level 0 and residue 0 and the initial rate, which stays in force until the first
    /// recalculation (round 500,000 on public networks), so it equals the rate carried by block 1.
    /// </summary>
    private static RewardsState GenesisRewardsState(BlockData firstBlock) =>
        new(0, 0, RewardsState.FromHeader(firstBlock.Block).Rate);

    /// <summary>
    /// Rebuilds the rewards state of the last published block when resuming. The block is re-fetched, which also
    /// proves that the archival source still serves the chain the database was built from. While following the chain the
    /// fetch gets the tip, so that a writer restarting at the tip can take the block from a relay (ADR 0019) instead of
    /// spending a request of the scarce archival nodes on every restart. A bounded run's target is not the tip: no hint.
    /// Whether relays are really asked is the fetcher's decision (only for rounds within its recent-rounds window of the tip).
    /// </summary>
    private async Task<RewardsState> SeedRewardsAsync(ChainStatus status, ulong? stopAfterRound, CancellationToken cancellationToken)
    {
        var seedRound = Math.Max(status.IndexedRound, 1UL);
        var seed = stopAfterRound is null
            ? await adapter.FetchBlockAsync(seedRound, await adapter.GetLatestRoundAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false)
            : await adapter.FetchBlockAsync(seedRound, cancellationToken).ConfigureAwait(false);
        if (stopAfterRound is null && !LinksToCursor(status, seed))
        {
            // The block may have come from a public relay, and certificate votes are not checked (ADR 0004): before telling the
            // operator to rebuild, ask the archival nodes alone. Only their answer decides a divergence.
            seed = await ArchivalIfForeignAsync(seed, s => LinksToCursor(status, s), cancellationToken).ConfigureAwait(false);
        }

        if (!LinksToCursor(status, seed))
        {
            throw new ChainDivergenceException(status.IndexedRound == 0
                ? "The archival source serves a different first block than the one the database was started from. The database must be rebuilt."
                : $"The archival source serves a different block {status.IndexedRound} than the one published. The database must be rebuilt.");
        }

        // Block 1 links back to the genesis block hash that was stored as the round 0 cursor; its rewards state starts at genesis.
        return status.IndexedRound == 0 ? GenesisRewardsState(seed) : RewardsState.FromHeader(seed.Block);
    }

    /// <summary>Whether the seed block is the published one (for round 0: whether block 1 links back to the stored genesis hash).</summary>
    private static bool LinksToCursor(ChainStatus status, BlockData seed) =>
        status.IndexedRound == 0
            ? seed.PreviousHash.AsSpan().SequenceEqual(status.IndexedBlockHash.Span)
            : seed.Hash.AsSpan().SequenceEqual(status.IndexedBlockHash.Span);

    /// <summary>Validates, normalizes and balances one block and stages the result; <see cref="FlushAsync"/> writes it.</summary>
    private async Task<RoundWrite> QueueBlockAsync(
        string chain,
        BalanceTracker tracker,
        ChainStatus previous,
        RewardsState previousRewards,
        BlockData block,
        ulong targetRound,
        CancellationToken cancellationToken)
    {
        if (block.Round != previous.IndexedRound + 1)
        {
            throw new ChainDivergenceException($"Expected round {previous.IndexedRound + 1} but received {block.Round}.");
        }

        if (!LinksTo(previous, block))
        {
            throw new ChainDivergenceException(
                $"Round {block.Round} does not link to the published round {previous.IndexedRound}. The database must be rebuilt.");
        }

        var normalized = normalizer.NormalizeBlock(block, previousRewards, await LoadReferencedAssetsAsync(block, cancellationToken).ConfigureAwait(false));
        var round = await tracker.ApplyAsync(normalized.Deltas, cancellationToken).ConfigureAwait(false);

        // Token records are written before the round is published: a crash in between only leaves rows that replaying the
        // round overwrites with the same values, whereas writing them afterwards could lose them for good.
        foreach (var token in normalized.TokenChanges)
        {
            if (token.Key.Kind == TokenKind.Arc200 && !await IsNewArc200Async(token, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await storage.UpsertAsync(token, cancellationToken).ConfigureAwait(false);
            if (token.Key.Kind == TokenKind.Arc200)
            {
                TrackArc200Metadata(token.Key.TokenId);
            }
        }

        // Metadata is read near the tip only: a backfill would ask for every token ever seen, at the pace of indexing.
        if (_arc200Pending.Count > 0 && targetRound - block.Round <= (ulong)_options.FollowingThresholdRounds)
        {
            await ReadPendingMetadataAsync(block, cancellationToken).ConfigureAwait(false);
        }

        var state = targetRound - block.Round <= (ulong)_options.FollowingThresholdRounds ? IngestionState.Following : IngestionState.Backfilling;
        var status = new ChainStatus(chain, block.Round, block.Hash, targetRound, state, _time.GetUtcNow());
        tracker.Stage(round);

        // Counted with the round only once it is queued: a round that fails before it never reaches a flush.
        _pendingMalformedArc200Logs += normalized.MalformedArc200Logs;
        return new RoundWrite(status, round.Effects);
    }

    private async Task TryFlushAsync(BalanceTracker tracker, List<RoundWrite> pending)
    {
        using var grace = new CancellationTokenSource(FlushGracePeriod);
        try
        {
            await FlushAsync(tracker, pending, grace.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The original failure is what the caller reports; the rounds are simply computed again after a restart.
            LogFlushFailed(logger, ex, pending.Count);
        }
    }

    /// <summary>Writes the staged rounds as one unit and lets the tracker cache their balances.</summary>
    private async Task FlushAsync(BalanceTracker tracker, List<RoundWrite> pending, CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        bool applied;
        var started = _time.GetTimestamp();
        try
        {
            applied = await storage.WriteRoundsAsync(pending, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _writeFailed = true;
            throw;
        }

        if (!applied)
        {
            // This indexer only ever writes the next rounds, so finding the first one published means a second writer is active;
            // its balances may differ from the ones computed here, which must not be cached.
            throw new ChainDivergenceException(
                $"Round {pending[0].Status.IndexedRound} of {pending[0].Status.Chain} was published by another writer. Only one indexer may write a chain.");
        }

        tracker.CommitStaged();
        _metrics.CommitRecorded(_time.GetElapsedTime(started), pending.Sum(static round => round.Effects.Count));
        _metrics.DecoderError(DecoderErrorKind.Arc200Log, _pendingMalformedArc200Logs);
        _pendingMalformedArc200Logs = 0;
        Interlocked.Add(ref _roundsCommitted, pending.Count);
        pending.Clear();
    }

    /// <summary>ARC-200 records are only written the first time a token is seen, so busy tokens cost no write per block.</summary>
    private async Task<bool> IsNewArc200Async(TokenInfo token, CancellationToken cancellationToken)
    {
        if (_knownArc200.Contains(token.Key.TokenId))
        {
            return false;
        }

        if (_arc200Pending.ContainsKey(token.Key.TokenId))
        {
            return false;
        }

        var stored = await storage.GetAsync(token.Key, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return true;
        }

        if (stored.Name is null && stored.Decimals is null)
        {
            TrackArc200Metadata(token.Key.TokenId);
        }
        else
        {
            _knownArc200.Add(token.Key.TokenId);
        }

        return false;
    }

    /// <summary>A token written without metadata is read later when the lookup is on; otherwise it is simply known.</summary>
    private void TrackArc200Metadata(ulong applicationId)
    {
        if (arc200Metadata is not null && _options.Arc200Metadata)
        {
            _arc200Pending.TryAdd(applicationId, DateTimeOffset.MinValue);
        }
        else
        {
            _knownArc200.Add(applicationId);
        }
    }

    /// <summary>After a failed lookup all lookups rest this long: the retry interval, at most a minute.</summary>
    private TimeSpan MetadataPause() => _options.Arc200MetadataRetryInterval < TimeSpan.FromMinutes(1) ? _options.Arc200MetadataRetryInterval : TimeSpan.FromMinutes(1);

    /// <summary>
    /// Reads the metadata of the pending tokens that are due, a few per block. It is enrichment: a failure, a hang (bounded here
    /// as well as in the source) or an unreachable node leaves the record as it is and is retried after
    /// <see cref="IndexerOptions.Arc200MetadataRetryInterval"/>; it never fails the round and never influences effects.
    /// </summary>
    private async Task ReadPendingMetadataAsync(BlockData block, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (now < _arc200LookupsPausedUntil || block.Block.FeeSink is not { } feeSink)
        {
            return;
        }

        var sender = feeSink.Bytes;

        var due = _arc200Pending.Where(pair => pair.Value <= now).Select(static pair => pair.Key).Take(MetadataLookupsPerBlock).ToList();
        if (due.Count == 0)
        {
            return;
        }

        ulong latest;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MetadataLookupTimeout);
            latest = await adapter.GetLatestRoundAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
        {
            LogMetadataUnavailable(logger, 0, ex.Message);
            _arc200LookupsPausedUntil = now + MetadataPause();
            return;
        }

        foreach (var applicationId in due)
        {
            Arc200Metadata? metadata = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(MetadataLookupTimeout);
                metadata = await arc200Metadata!.TryReadAsync(applicationId, sender, latest, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
            {
                LogMetadataUnavailable(logger, applicationId, ex.Message);
            }

            if (metadata is null)
            {
                // The node could not be asked: a slow or down algod must not cost every block a timeout, so pause all lookups for a while.
                _arc200Pending[applicationId] = now + _options.Arc200MetadataRetryInterval;
                _arc200LookupsPausedUntil = now + MetadataPause();
                return;
            }

            if (metadata.Name is null && metadata.Symbol is null && metadata.Decimals is null && metadata.TotalSupply is null)
            {
                // Nothing answered: the application may not exist on this node yet (it lags the relays) or does not implement ARC-200.
                var empty = _arc200EmptyAnswers.GetValueOrDefault(applicationId) + 1;
                _arc200EmptyAnswers[applicationId] = empty;
                if (empty < MaxEmptyAnswers)
                {
                    _arc200Pending[applicationId] = now + (_options.Arc200MetadataRetryInterval * empty);
                    continue;
                }

                _arc200Pending.Remove(applicationId);
                _arc200EmptyAnswers.Remove(applicationId);
                _knownArc200.Add(applicationId);
                continue;
            }

            // The sets change only after the write: a failing write leaves the token pending, and (enrichment never fails the round)
            // lookups rest for a while.
            try
            {
                var key = TokenKey.Arc200(applicationId);
                if (await storage.GetAsync(key, cancellationToken).ConfigureAwait(false) is { } stored)
                {
                    await storage.UpsertAsync(WithMetadata(stored, metadata), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
            {
                LogMetadataUnavailable(logger, applicationId, ex.Message);
                _arc200LookupsPausedUntil = now + MetadataPause();
                return;
            }

            // A partial answer is kept, and asked again a few times (a lagging node may have answered only some methods).
            var attempts = _arc200EmptyAnswers.GetValueOrDefault(applicationId) + 1;
            if (!metadata.Complete && attempts < MaxEmptyAnswers)
            {
                _arc200EmptyAnswers[applicationId] = attempts;
                _arc200Pending[applicationId] = now + (_options.Arc200MetadataRetryInterval * attempts);
                continue;
            }

            _arc200Pending.Remove(applicationId);
            _arc200EmptyAnswers.Remove(applicationId);
            _knownArc200.Add(applicationId);
        }
    }

    private static TokenInfo WithMetadata(TokenInfo stored, Arc200Metadata metadata) =>
        stored with
        {
            Name = metadata.Name ?? stored.Name,
            Symbol = metadata.Symbol ?? stored.Symbol,
            Decimals = metadata.Decimals ?? stored.Decimals,
            StandardVerified = metadata.Complete,
            MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["totalSupply"] = metadata.TotalSupply?.ToString(),
                ["metadataSource"] = "simulate",
            }),
        };

    private async Task<AssetDirectory> LoadReferencedAssetsAsync(BlockData block, CancellationToken cancellationToken)
    {
        // Destroying and reconfiguring an asset both need the record written when it was created.
        var referenced = AssetScanner.ReferencedAssets(block);
        if (referenced.Count == 0)
        {
            return AssetDirectory.Empty;
        }

        var records = new List<TokenInfo>(referenced.Count);
        foreach (var assetId in referenced)
        {
            if (await storage.GetAsync(TokenKey.Asa(assetId), cancellationToken).ConfigureAwait(false) is { } record)
            {
                records.Add(record);
            }
        }

        return new AssetDirectory(records);
    }

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning, Message = "Could not write {Rounds} computed rounds while stopping; they are indexed again on restart")]
    private static partial void LogFlushFailed(ILogger logger, Exception exception, int rounds);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "{Chain}: indexed round {Round} of {Target}")]
    private static partial void LogProgress(ILogger logger, string chain, ulong round, ulong target);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "{Chain}: genesis applied with {Accounts} funded accounts")]
    private static partial void LogGenesis(ILogger logger, string chain, int accounts);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Warning, Message = "algod reports round {Round} as the latest since {Since:u}; the chain or the node is stuck, the status is no longer refreshed")]
    private static partial void LogTipStale(ILogger logger, ulong round, DateTimeOffset since);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Information, Message = "algod advances again (latest round {Round}); the status is refreshed again")]
    private static partial void LogTipAdvancing(ILogger logger, ulong round);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Debug, Message = "ARC-200 metadata of application {ApplicationId} could not be read now: {Reason}")]
    private static partial void LogMetadataUnavailable(ILogger logger, ulong applicationId, string reason);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug, Message = "Block {Round} is not available on archival nodes yet; waiting")]
    private static partial void LogWaitingForBlock(ILogger logger, ulong round);
}

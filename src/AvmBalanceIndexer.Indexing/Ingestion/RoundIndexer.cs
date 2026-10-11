using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using System.Runtime.ExceptionServices;
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
    TimeProvider? timeProvider = null)
{
    private readonly IndexerOptions _options = options.Value;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long _roundsCommitted;

    /// <summary>Total rounds this instance has committed; lets a supervisor tell progress from a stall without reading storage.</summary>
    public long RoundsCommitted => Interlocked.Read(ref _roundsCommitted);

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
                    rewards = await SeedRewardsAsync(status, cancellationToken).ConfigureAwait(false);
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

            if (next > target)
            {
                if (stopAfterRound is not null)
                {
                    break;
                }

                // Caught up: keep the published target and timestamp moving so an idle indexer is distinguishable from a stalled one.
                status = status with { TargetRound = latest, State = IngestionState.Following, UpdatedAt = _time.GetUtcNow() };
                await storage.UpdateProgressAsync(status, cancellationToken).ConfigureAwait(false);
                await Task.Delay(_options.FollowPollInterval, _time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await foreach (var block in OrderedPrefetch.StreamAsync(
                    next, target, _options.FetchConcurrency, adapter.FetchBlockAsync, cancellationToken).ConfigureAwait(false))
                {
                    var written = await IndexBlockAsync(chain, tracker, status, rewards, block, target, cancellationToken).ConfigureAwait(false);
                    status = written.Status;
                    rewards = RewardsState.FromHeader(block.Block);
                    next = block.Round + 1;
                    rounds++;
                    Interlocked.Increment(ref _roundsCommitted);
                    effects += written.EffectCount;

                    if (rounds % _options.LogEveryRounds == 0)
                    {
                        LogProgress(logger, chain, block.Round, target);
                    }
                }
            }
            catch (BlockNotAvailableException ex) when (stopAfterRound is null)
            {
                // At the tip the archival nodes may not have the newest block yet; wait and ask again. A block far behind the
                // tip that nobody serves is a stall, not a wait.
                await WaitIfJustMissingAsync(ex, cancellationToken).ConfigureAwait(false);
            }
        }

        return new IndexingResult(first, status.IndexedRound, rounds, effects);
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
    /// proves that the archival source still serves the chain the database was built from.
    /// </summary>
    private async Task<RewardsState> SeedRewardsAsync(ChainStatus status, CancellationToken cancellationToken)
    {
        var seedRound = Math.Max(status.IndexedRound, 1UL);
        var seed = await adapter.FetchBlockAsync(seedRound, cancellationToken).ConfigureAwait(false);
        if (status.IndexedRound == 0)
        {
            // Block 1 links back to the genesis block hash that was stored as the round 0 cursor.
            return seed.PreviousHash.AsSpan().SequenceEqual(status.IndexedBlockHash.Span)
                ? GenesisRewardsState(seed)
                : throw new ChainDivergenceException(
                    "The archival source serves a different first block than the one the database was started from. The database must be rebuilt.");
        }

        return seed.Hash.AsSpan().SequenceEqual(status.IndexedBlockHash.Span)
            ? RewardsState.FromHeader(seed.Block)
            : throw new ChainDivergenceException(
                $"The archival source serves a different block {status.IndexedRound} than the one published. The database must be rebuilt.");
    }

    private async Task<(ChainStatus Status, int EffectCount)> IndexBlockAsync(
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

        if (!block.PreviousHash.AsSpan().SequenceEqual(previous.IndexedBlockHash.Span))
        {
            throw new ChainDivergenceException(
                $"Round {block.Round} does not link to the published round {previous.IndexedRound}. The database must be rebuilt.");
        }

        var normalized = normalizer.NormalizeBlock(block, previousRewards, await LoadDestroyedAssetsAsync(block, cancellationToken).ConfigureAwait(false));
        var round = await tracker.ApplyAsync(normalized.Deltas, cancellationToken).ConfigureAwait(false);

        // Token records are written before the round is published: a crash in between only leaves rows that replaying the
        // round overwrites with the same values, whereas writing them afterwards could lose them for good.
        foreach (var token in normalized.TokenChanges)
        {
            await storage.UpsertAsync(token, cancellationToken).ConfigureAwait(false);
        }

        var state = targetRound - block.Round <= (ulong)_options.FollowingThresholdRounds ? IngestionState.Following : IngestionState.Backfilling;
        var status = new ChainStatus(chain, block.Round, block.Hash, targetRound, state, _time.GetUtcNow());
        if (!await storage.WriteRoundAsync(status, round.Effects, cancellationToken).ConfigureAwait(false))
        {
            // This indexer only ever writes the next round, so finding it published means a second writer is active; its
            // balances may differ from the ones computed here, which must not be cached.
            throw new ChainDivergenceException($"Round {block.Round} of {chain} was published by another writer. Only one indexer may write a chain.");
        }

        tracker.Commit(round);
        return (status, round.Effects.Count);
    }

    private async Task<AssetDirectory> LoadDestroyedAssetsAsync(BlockData block, CancellationToken cancellationToken)
    {
        var destroyed = AssetScanner.DestroyedAssets(block);
        if (destroyed.Count == 0)
        {
            return AssetDirectory.Empty;
        }

        var records = new List<TokenInfo>(destroyed.Count);
        foreach (var assetId in destroyed)
        {
            if (await storage.GetAsync(TokenKey.Asa(assetId), cancellationToken).ConfigureAwait(false) is { } record)
            {
                records.Add(record);
            }
        }

        return new AssetDirectory(records);
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "{Chain}: indexed round {Round} of {Target}")]
    private static partial void LogProgress(ILogger logger, string chain, ulong round, ulong target);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "{Chain}: genesis applied with {Accounts} funded accounts")]
    private static partial void LogGenesis(ILogger logger, string chain, int accounts);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug, Message = "Block {Round} is not available on archival nodes yet; waiting")]
    private static partial void LogWaitingForBlock(ILogger logger, ulong round);
}

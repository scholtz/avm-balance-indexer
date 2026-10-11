using System.Globalization;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Cassandra;

namespace AvmBalanceIndexer.Storage.Cassandra;

public sealed partial class CassandraChainStorage
{
    public async Task<bool> WriteRoundAsync(ChainStatus roundStatus, IReadOnlyList<TokenEffect> effects, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roundStatus);
        ArgumentNullException.ThrowIfNull(effects);
        EnsureChain(roundStatus.Chain);

        var round = roundStatus.IndexedRound;
        if (effects.Any(e => e.Id.Round != round))
        {
            throw new ArgumentException($"All effects must belong to round {round}.", nameof(effects));
        }

        // Upserts would silently merge two effects with one identity; the contract is to fail the round instead.
        var identities = new HashSet<(Address, TokenKey, int, int)>();
        foreach (var effect in effects)
        {
            if (!identities.Add((effect.Account, effect.Token, effect.Id.IntraRoundOrder, effect.Id.Ordinal)))
            {
                throw new ArgumentException(
                    $"Two effects of {effect.Account}/{effect.Token} share the identity ({round}, {effect.Id.IntraRoundOrder}, {effect.Id.Ordinal}).",
                    nameof(effects));
            }
        }

        var published = await ReadCursorAsync(serial: true, cancellationToken).ConfigureAwait(false);
        if (published is null)
        {
            if (round != 0)
            {
                throw new ChainDivergenceException($"The first round written must be 0 but round {round} was given.");
            }
        }
        else if (round == published.Value.Round)
        {
            if (!roundStatus.IndexedBlockHash.Span.SequenceEqual(published.Value.Hash))
            {
                throw new ChainDivergenceException($"Round {round} is already published with a different block hash.");
            }

            // Idempotent replay of the latest published round: nothing to do.
            return false;
        }
        else if (round < published.Value.Round)
        {
            throw new ChainDivergenceException(
                $"Round {round} is older than the published round {published.Value.Round}; its block hash can no longer be verified, so it is refused.");
        }
        else if (round != published.Value.Round + 1)
        {
            throw new ChainDivergenceException($"Round {round} would leave a gap after published round {published.Value.Round}.");
        }

        if (effects.Count > 0)
        {
            await WriteEffectsAsync(round, effects, cancellationToken).ConfigureAwait(false);
        }

        await PublishAsync(roundStatus, published, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task WriteEffectsAsync(ulong round, IReadOnlyList<TokenEffect> effects, CancellationToken cancellationToken)
    {
        var bucket = RoundBuckets.ForRound(round);
        var latest = effects
            .GroupBy(static e => new HoldingKey(e.Account, e.Token))
            .ToDictionary(static g => g.Key, static g => g.MaxBy(static e => e.Id.Ordinal)!);

        // Accounts without a holding row have never been touched in this token; they get their first_round recorded.
        // Read before anything is written: a replay after a crash must see the same answer.
        var keys = latest.Keys.ToArray();
        var previous = await RunBoundedAsync(
            keys,
            key => HoldingRoundAsync(key, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var firstTimers = keys.Where((_, i) => previous[i] is null).ToArray();

        // The bucket index only changes when an account moves into a new bucket (about once per 100,000 rounds).
        var newBuckets = keys.Where((_, i) => previous[i] is not { } last || RoundBuckets.ForRound(last) != bucket).ToArray();

        // Order matters for crash recovery: a holding row is written last, so every holding implies its touched row.
        var insertTokenEffect = $"""
            INSERT INTO {Table("effects_by_token_bucket")}
              (token_kind, token_id, bucket, shard, round, intra_round_order, ordinal, account, transaction_id, effect_type, delta)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        var insertBucket = $"INSERT INTO {Table("effect_buckets_by_account_token")} (account, token_kind, token_id, bucket) VALUES (?, ?, ?, ?)";
        var insertTouched = $"INSERT INTO {Table("accounts_touched_by_token")} (token_kind, token_id, shard, account, first_round) VALUES (?, ?, ?, ?, ?)";
        var roundValue = unchecked((long)round);

        var statements = new List<Func<Task>>(effects.Count * 2 + keys.Length * 2);
        foreach (var effect in effects)
        {
            var effectCopy = effect;
            var kind = effect.Token.Kind.ToStorageName();
            var tokenId = StorageId(effect.Token.TokenId);
            var delta = effect.Delta.ToString(CultureInfo.InvariantCulture);
            statements.Add(() => InsertAccountEffectAsync(effectCopy, bucket, delta, cancellationToken));
            statements.Add(() => ExecuteAsync(
                insertTokenEffect,
                cancellationToken,
                kind,
                tokenId,
                bucket,
                ShardOf(effectCopy.Account),
                roundValue,
                effectCopy.Id.IntraRoundOrder,
                effectCopy.Id.Ordinal,
                effectCopy.Account.ToArray(),
                effectCopy.Id.TransactionId,
                effectCopy.Type.ToString(),
                delta));
        }

        foreach (var key in newBuckets)
        {
            var keyCopy = key;
            statements.Add(() => ExecuteAsync(
                insertBucket,
                cancellationToken,
                keyCopy.Account.ToArray(),
                keyCopy.Token.Kind.ToStorageName(),
                StorageId(keyCopy.Token.TokenId),
                bucket));
        }

        foreach (var key in firstTimers)
        {
            var keyCopy = key;
            statements.Add(() => ExecuteAsync(
                insertTouched,
                cancellationToken,
                keyCopy.Token.Kind.ToStorageName(),
                StorageId(keyCopy.Token.TokenId),
                ShardOf(keyCopy.Account),
                keyCopy.Account.ToArray(),
                roundValue));
        }

        await RunBoundedAsync(statements, static statement => statement(), cancellationToken).ConfigureAwait(false);

        // The last effect of a round per account/token carries the end-of-round absolute balance. USING TIMESTAMP makes
        // the newest round win even when a stale writer is still replaying an older one.
        var upsertHolding = $"""
            INSERT INTO {Table("holdings_by_account")} (account, token_kind, token_id, amount, opted_in, last_changed_round)
            VALUES (?, ?, ?, ?, ?, ?) USING TIMESTAMP ?
            """;
        var timestamp = unchecked((long)round) + 1;
        await RunBoundedAsync(
            latest.Values.ToArray(),
            effect => ExecuteAsync(
                upsertHolding,
                cancellationToken,
                effect.Account.ToArray(),
                effect.Token.Kind.ToStorageName(),
                StorageId(effect.Token.TokenId),
                effect.BalanceAfter.ToString(),
                effect.Type != EffectType.AssetOptOut,
                roundValue,
                timestamp),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Optional columns are left out of the statement instead of being bound to null: a null cell is a tombstone, and about
    /// every other effect (fees) has no counterparty.
    /// </summary>
    private Task<RowSet> InsertAccountEffectAsync(TokenEffect effect, int bucket, string delta, CancellationToken cancellationToken)
    {
        var columns = "account, token_kind, token_id, bucket, round, intra_round_order, ordinal, transaction_id, effect_type, delta, balance_after";
        var markers = "?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?";
        var values = new List<object?>
        {
            effect.Account.ToArray(),
            effect.Token.Kind.ToStorageName(),
            StorageId(effect.Token.TokenId),
            bucket,
            unchecked((long)effect.Id.Round),
            effect.Id.IntraRoundOrder,
            effect.Id.Ordinal,
            effect.Id.TransactionId,
            effect.Type.ToString(),
            delta,
            effect.BalanceAfter.ToString(),
        };
        if (effect.Id.InnerPath.Count > 0)
        {
            columns += ", inner_path";
            markers += ", ?";
            values.Add(effect.Id.InnerPath.ToArray());
        }

        if (effect.Counterparty is { } counterparty)
        {
            columns += ", counterparty";
            markers += ", ?";
            values.Add(counterparty.ToArray());
        }

        return ExecuteAsync($"INSERT INTO {Table("effects_by_account_token_bucket")} ({columns}) VALUES ({markers})", cancellationToken, [.. values]);
    }

    /// <summary>Round of the last write to the holding row, or null when the account never held the token.</summary>
    private async Task<ulong?> HoldingRoundAsync(HoldingKey key, CancellationToken cancellationToken)
    {
        var rows = await ExecuteAsync(
            $"SELECT last_changed_round FROM {Table("holdings_by_account")} WHERE account = ? AND token_kind = ? AND token_id = ?",
            cancellationToken,
            key.Account.ToArray(),
            key.Token.Kind.ToStorageName(),
            StorageId(key.Token.TokenId)).ConfigureAwait(false);
        return rows.FirstOrDefault() is { } row ? (ulong)row.GetValue<long>("last_changed_round") : null;
    }

    /// <summary>
    /// Publishes the round with a compare-and-set on the cursor, so a second writer racing on the same round loses instead of
    /// moving the cursor twice.
    /// </summary>
    private async Task PublishAsync(ChainStatus status, (ulong Round, byte[] Hash)? previous, CancellationToken cancellationToken)
    {
        BoundStatement statement;
        if (previous is null)
        {
            statement = await BindAsync(
                $"""
                INSERT INTO {Table("chain_status")} (chain, indexed_round, indexed_block_hash, target_round, status, updated_at)
                VALUES (?, ?, ?, ?, ?, ?) IF NOT EXISTS
                """,
                cancellationToken,
                status.Chain,
                unchecked((long)status.IndexedRound),
                status.IndexedBlockHash.ToArray(),
                status.TargetRound is { } target ? unchecked((long)target) : null,
                status.State.ToString(),
                status.UpdatedAt).ConfigureAwait(false);
        }
        else
        {
            statement = await BindAsync(
                $"""
                UPDATE {Table("chain_status")}
                SET indexed_round = ?, indexed_block_hash = ?, target_round = ?, status = ?, updated_at = ?
                WHERE chain = ? IF indexed_round = ? AND indexed_block_hash = ?
                """,
                cancellationToken,
                unchecked((long)status.IndexedRound),
                status.IndexedBlockHash.ToArray(),
                status.TargetRound is { } target ? unchecked((long)target) : null,
                status.State.ToString(),
                status.UpdatedAt,
                status.Chain,
                unchecked((long)previous.Value.Round),
                previous.Value.Hash).ConfigureAwait(false);
        }

        statement.SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial);
        var result = await ExecuteAsync(statement, cancellationToken).ConfigureAwait(false);
        if (!result.First().GetValue<bool>("[applied]"))
        {
            throw new ChainDivergenceException($"Round {status.IndexedRound} could not be published because another writer moved the cursor first.");
        }
    }

    /// <summary>
    /// The writer reads the cursor with SERIAL so it completes any lightweight transaction still in flight. Readers use
    /// LOCAL_QUORUM instead: a publication is committed at LOCAL_QUORUM before it is acknowledged, so a quorum read after
    /// it sees it, and the hot cursor partition is not hit with a Paxos round per API request.
    /// </summary>
    private async Task<(ulong Round, byte[] Hash)?> ReadCursorAsync(bool serial, CancellationToken cancellationToken)
    {
        var statement = await BindAsync(
            $"SELECT indexed_round, indexed_block_hash FROM {Table("chain_status")} WHERE chain = ?",
            cancellationToken,
            _chain).ConfigureAwait(false);
        if (serial)
        {
            statement.SetConsistencyLevel(ConsistencyLevel.LocalSerial);
        }

        var row = (await ExecuteAsync(statement, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        return row is null ? null : ((ulong)row.GetValue<long>("indexed_round"), row.GetValue<byte[]>("indexed_block_hash"));
    }
}

using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>A balance the database holds for a round that is not the one the ledger holds.</summary>
/// <param name="Account">The account.</param>
/// <param name="Token">The token (native or an ASA).</param>
/// <param name="Indexed">The balance the database holds after the round.</param>
/// <param name="Ledger">The balance the ledger holds after the round (zero for a removed holding).</param>
public sealed record LedgerMismatch(Address Account, TokenKey Token, Amount Indexed, Amount Ledger);

/// <summary>
/// Compares what the database holds after a round with the ledger's own post-round balances (ADR 0021, "Finding R early"). The comparison is
/// absolute: the database holds the whole state from genesis, so the end-of-round balance of every account the ledger changed must be equal,
/// including closed accounts and removed holdings (zero). ARC-200 balances are application state whose layout differs per contract, and are not
/// audited. The reads are pinned to the round, never current holdings.
/// </summary>
public static class LedgerAudit
{
    /// <summary>The balances of every account and asset the ledger changed in <paramref name="round"/> that differ in the database.</summary>
    /// <param name="storage">The database.</param>
    /// <param name="delta">The ledger's post-round balances.</param>
    /// <param name="round">The round the delta belongs to (it must be published).</param>
    /// <param name="concurrency">Reads in flight at once.</param>
    /// <param name="cancellationToken">Cancels the comparison.</param>
    public static async Task<IReadOnlyList<LedgerMismatch>> CompareAsync(IBalanceReadStore storage, LedgerDelta delta, ulong round, int concurrency, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(delta);

        var checks = new List<(Address Account, TokenKey Token, ulong Ledger)>(delta.Algo.Count + delta.Assets.Count);
        checks.AddRange(delta.Algo.Select(static pair => (pair.Key, TokenKey.Native, pair.Value)));
        checks.AddRange(delta.Assets.Select(static pair => (pair.Key.Account, TokenKey.Asa(pair.Key.AssetId), pair.Value ?? 0UL)));

        var mismatches = new List<LedgerMismatch>();
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        await Task.WhenAll(checks.Select(async check =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var indexed = await storage.GetBalanceAtRoundAsync(check.Account, check.Token, round, cancellationToken).ConfigureAwait(false);
                if (indexed.Value != check.Ledger)
                {
                    lock (mismatches)
                    {
                        mismatches.Add(new LedgerMismatch(check.Account, check.Token, indexed, Amount.From(check.Ledger)));
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return [.. mismatches.OrderBy(static m => m.Token.TokenId).ThenBy(static m => m.Account.ToString(), StringComparer.Ordinal)];
    }
}

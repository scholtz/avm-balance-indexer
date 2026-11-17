using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using Xunit;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// The comparison of two databases that must be equal after a partial rebuild (ADR 0021): a database that was rewound or forked at a round
/// against one that was indexed straight to it. Token records are compared by existence, creation and deletion only.
/// </summary>
public static class RebuildAssertions
{
    private static readonly Address[] Accounts = [Scenario.Alice, Scenario.Bob, Scenario.Carol, Scenario.Dave];

    /// <summary>The hash of the published block of a database that was indexed to <paramref name="round"/>.</summary>
    public static async Task<byte[]> HashOfAsync(IChainStorage reference, ulong round)
    {
        var status = await reference.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None);
        return status is not null && status.IndexedRound == round
            ? status.IndexedBlockHash.ToArray()
            : throw new InvalidOperationException("The reference must be indexed to the round.");
    }

    /// <summary>Asserts that everything the contract can read up to <paramref name="upTo"/> is the same.</summary>
    /// <param name="actual">The rewound or forked database.</param>
    /// <param name="expected">The database that was indexed straight to the round.</param>
    /// <param name="upTo">The round of the rebuild.</param>
    /// <param name="tokens">Tokens whose existence, creation and deletion are compared.</param>
    public static async Task AssertSameAsync(IChainStorage actual, IChainStorage expected, ulong upTo, params TokenKey[] tokens)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        var a = await actual.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None);
        var e = await expected.GetAsync(ChainId.AlgorandTestnet, CancellationToken.None);
        Assert.Equal(e!.IndexedRound, a!.IndexedRound);
        Assert.Equal(e.IndexedBlockHash.ToArray(), a.IndexedBlockHash.ToArray());

        foreach (var account in Accounts)
        {
            Assert.Equal(await HoldingsAsync(expected, account, false), await HoldingsAsync(actual, account, false));
            Assert.Equal(await HoldingsAsync(expected, account, true), await HoldingsAsync(actual, account, true));
            Assert.Equal(await EffectsAsync(expected, account), await EffectsAsync(actual, account));
            for (ulong round = 0; round <= upTo; round++)
            {
                Assert.Equal(
                    (await expected.GetBalanceAtRoundAsync(account, TokenKey.Native, round, CancellationToken.None)).Value,
                    (await actual.GetBalanceAtRoundAsync(account, TokenKey.Native, round, CancellationToken.None)).Value);
            }
        }

        var keys = Accounts.Select(static account => new HoldingKey(account, TokenKey.Native)).ToArray();
        Assert.Equal(
            (await expected.GetCurrentHoldingsAsync(keys, CancellationToken.None)).OrderBy(static pair => pair.Key.Account.ToString(), StringComparer.Ordinal).Select(static pair => $"{pair.Key.Account}={pair.Value.Amount.Value}/{pair.Value.OptedIn}/{pair.Value.LastChangedRound}").ToArray(),
            (await actual.GetCurrentHoldingsAsync(keys, CancellationToken.None)).OrderBy(static pair => pair.Key.Account.ToString(), StringComparer.Ordinal).Select(static pair => $"{pair.Key.Account}={pair.Value.Amount.Value}/{pair.Value.OptedIn}/{pair.Value.LastChangedRound}").ToArray());

        for (ulong round = 0; round <= upTo; round++)
        {
            foreach (var includeZero in new[] { false, true })
            {
                Assert.Equal(await HoldersAsync(expected, round, includeZero), await HoldersAsync(actual, round, includeZero));
            }
        }

        foreach (var token in tokens)
        {
            var expectedToken = await expected.GetAsync(token, CancellationToken.None);
            var actualToken = await actual.GetAsync(token, CancellationToken.None);
            Assert.Equal(expectedToken is null, actualToken is null);
            Assert.Equal((expectedToken?.CreatedRound, expectedToken?.DeletedRound), (actualToken?.CreatedRound, actualToken?.DeletedRound));
        }
    }

    private static async Task<string[]> HoldingsAsync(IChainStorage storage, Address account, bool includeZero) =>
        [.. (await storage.GetHoldingsAsync(account, includeZero, null, 100, CancellationToken.None)).Items
            .Select(static h => $"{h.Token.Kind}:{h.Token.TokenId}={h.Amount.Value}/{h.OptedIn}/{h.LastChangedRound}")];

    private static async Task<string[]> EffectsAsync(IChainStorage storage, Address account) =>
        [.. (await storage.GetEffectsAsync(account, TokenKey.Native, null, null, null, 100, CancellationToken.None)).Items
            .Select(static x => $"{x.Id.Round}.{x.Id.IntraRoundOrder}.{x.Id.Ordinal}.{x.Id.TransactionId}:{x.Type}:{x.Delta}->{x.BalanceAfter.Value}")];

    private static async Task<string[]> HoldersAsync(IChainStorage storage, ulong round, bool includeZero) =>
        [.. (await storage.GetHoldersAtRoundAsync(TokenKey.Native, round, includeZero, null, 100, CancellationToken.None)).Items
            .Select(static h => $"{h.Account}={h.Amount.Value}")];
}

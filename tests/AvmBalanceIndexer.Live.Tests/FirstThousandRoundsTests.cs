using System.Collections.Concurrent;
using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Live.Tests;

/// <summary>
/// Indexes the first 1000 rounds of real chains from their archival nodes (Algorand5 gossip block fetcher) into a
/// database and proves the native token balances against the ledger itself.
/// </summary>
/// <remarks>
/// The reference is the ledger state delta algod publishes for every round, which holds the exact post-round balance
/// of every account that round changed. Transaction ids and amounts reported by the public indexer are checked as an
/// independent cross-reference.
/// TestNet has real payments in this range. MainNet has none, so its balances only move through the rewards pool.
/// </remarks>
[Collection(PostgresServerDefinition.Name)]
public class FirstThousandRoundsTests(PostgresFixture postgres)
{
    private const ulong Rounds = 1000;

    private static readonly TokenKey Native = TokenKey.Native;
    private static readonly ConcurrentDictionary<string, Lazy<CachedChainAdapter>> Adapters = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<ulong, IReadOnlyDictionary<Address, ulong>>>>> Ledgers = new();

    [LiveFact]
    public async Task TestNet_First_1000_Rounds_Match_The_Ledger_In_Memory()
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandTestnet));

        await IndexAndVerifyAsync(ChainId.AlgorandTestnet, storage, expectPayments: true);
    }

    [LiveFact]
    public async Task TestNet_First_1000_Rounds_Match_The_Ledger_In_PostgreSql()
    {
        var storage = await postgres.CreateStorageAsync(ChainId.AlgorandTestnet);

        await IndexAndVerifyAsync(ChainId.AlgorandTestnet, storage, expectPayments: true);
    }

    [LiveFact]
    public async Task MainNet_First_1000_Rounds_Match_The_Ledger_In_Memory()
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.AlgorandMainnet));

        await IndexAndVerifyAsync(ChainId.AlgorandMainnet, storage, expectPayments: false);
    }

    [LiveFact]
    public async Task MainNet_First_1000_Rounds_Match_The_Ledger_In_PostgreSql()
    {
        var storage = await postgres.CreateStorageAsync(ChainId.AlgorandMainnet);

        await IndexAndVerifyAsync(ChainId.AlgorandMainnet, storage, expectPayments: false);
    }

    /// <summary>
    /// Voi publishes no historical ledger deltas, so it is checked against what a public indexer reports. Its first 1000
    /// rounds only contain transactions without balance impact, so this mainly proves the pinned genesis, block header hashes,
    /// hash chaining and genesis balances. Its archival nodes are patchy, which the fetcher has to absorb by failing over.
    /// </summary>
    [LiveFact]
    public async Task Voi_First_1000_Rounds_Match_The_Indexer_In_Memory()
    {
        var storage = new InMemoryChainStorage(new ChainContext(ChainId.Voi));

        await IndexAndVerifyAsync(ChainId.Voi, storage, expectPayments: false, ledgerDeltas: false);
    }

    [LiveFact]
    public async Task Resuming_Midway_Through_The_Real_Chain_Gives_The_Same_Balances()
    {
        var chain = ChainId.AlgorandTestnet;
        var straight = new InMemoryChainStorage(new ChainContext(chain));
        var resumed = new InMemoryChainStorage(new ChainContext(chain));
        var adapter = Adapter(chain);

        await CreateIndexer(straight, adapter).RunAsync(Rounds, CancellationToken.None);
        await CreateIndexer(resumed, adapter).RunAsync(200, CancellationToken.None);
        var rest = await CreateIndexer(resumed, adapter).RunAsync(Rounds, CancellationToken.None);

        Assert.Equal((201UL, Rounds, 800L), (rest.FirstRound, rest.LastRound, rest.RoundsIndexed));
        var expected = await AllHoldersAsync(straight, Rounds);
        var actual = await AllHoldersAsync(resumed, Rounds);
        Assert.Equal(expected.Select(static h => (h.Account, h.Amount)), actual.Select(static h => (h.Account, h.Amount)));
    }

    internal static async Task IndexAndVerifyAsync(string chain, IChainStorage storage, bool expectPayments, bool ledgerDeltas = true)
    {
        var adapter = Adapter(chain);
        var result = await CreateIndexer(storage, adapter).RunAsync(Rounds, CancellationToken.None);

        Assert.Equal((1UL, Rounds, (long)Rounds), (result.FirstRound, result.LastRound, result.RoundsIndexed));
        var status = await storage.GetAsync(chain, CancellationToken.None);
        Assert.Equal(Rounds, status!.IndexedRound);
        Assert.Equal((await adapter.FetchBlockAsync(Rounds, CancellationToken.None)).Hash, status.IndexedBlockHash.ToArray());

        var genesis = await adapter.GetGenesisAsync(CancellationToken.None);
        var holders = await AllHoldersAsync(storage, Rounds);
        Assert.Equal(genesis.Accounts.Count(static a => a.Balance > 0), holders.Count(h => genesis.Accounts.Any(a => a.Address == h.Account)));

        if (ledgerDeltas)
        {
            await AssertEveryRoundMatchesTheLedgerAsync(chain, storage, holders);
        }

        await AssertTransactionsMatchTheIndexerAsync(chain, storage, holders, expectPayments, genesis);
    }

    /// <summary>
    /// For every round, every account the ledger says changed must have exactly the ledger balance in our index at the
    /// end of that round, and every balance change in our index must correspond to an account the ledger changed.
    /// </summary>
    private static async Task AssertEveryRoundMatchesTheLedgerAsync(string chain, IChainStorage storage, IReadOnlyList<Holding> holders)
    {
        var ledger = await LedgerAsync(chain);
        var mismatches = new List<string>();

        for (ulong round = 1; round <= Rounds; round++)
        {
            foreach (var (account, balance) in ledger[round])
            {
                var ours = await storage.GetBalanceAtRoundAsync(account, Native, round, CancellationToken.None);
                if (ours.Value != balance)
                {
                    mismatches.Add($"round {round} {ReferenceNode.Encode(account)}: ledger {balance} but indexed {ours}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} balances differ from the ledger:{Environment.NewLine}{string.Join(Environment.NewLine, mismatches.Take(20))}");

        var unexpected = new List<string>();
        foreach (var holder in holders)
        {
            foreach (var effect in await AllEffectsAsync(storage, holder.Account))
            {
                if (effect.Id.Round > 0 && !ledger[effect.Id.Round].ContainsKey(effect.Account))
                {
                    unexpected.Add($"round {effect.Id.Round} {ReferenceNode.Encode(effect.Account)} {effect.Type} {effect.Delta}");
                }
            }
        }

        Assert.True(unexpected.Count == 0, $"Indexed changes the ledger did not make:{Environment.NewLine}{string.Join(Environment.NewLine, unexpected.Take(20))}");
    }

    /// <summary>The indexed transactions are exactly the transactions the independent indexer reports, with the same ids.</summary>
    private static async Task AssertTransactionsMatchTheIndexerAsync(string chain, IChainStorage storage, IReadOnlyList<Holding> holders, bool expectPayments, GenesisDocument genesis)
    {
        using var reference = Reference(chain);
        var all = await reference.TransactionsUpToAsync(Rounds);

        // A transaction without any native balance impact (no fee, no amount, no rewards) leaves no effect behind.
        var expected = all
            .Where(static t => t.Fee > 0 || t.Amount > 0 || t.ClosingAmount > 0 || t.SenderRewards > 0 || t.ReceiverRewards > 0 || t.CloseRewards > 0)
            .ToList();
        Assert.Equal(expectPayments, expected.Count > 0);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var holder in holders)
        {
            foreach (var effect in await AllEffectsAsync(storage, holder.Account))
            {
                if (effect.Id.TransactionId.Length > 0)
                {
                    ids.Add(effect.Id.TransactionId);
                }
            }
        }

        Assert.Equal(expected.Select(static t => t.Id).Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));

        // Independent replay of the indexer view: genesis balance plus every reported transaction effect, fees credited
        // to the fee sink. The rewards pool is excluded because its protocol withdrawals are not transactions.
        var feeSink = genesis.Accounts.Single(static a => a.Comment == "FeeSink").Address;
        var pool = genesis.Accounts.Single(static a => a.Comment == "RewardsPool").Address;
        var replayed = new Dictionary<Address, BigInteger>();
        foreach (var transaction in expected.OrderBy(static t => t.Round))
        {
            Add(replayed, transaction.Sender, -(BigInteger)transaction.Fee - transaction.Amount - transaction.ClosingAmount + transaction.SenderRewards);
            Add(replayed, feeSink, transaction.Fee);
            if (transaction.Receiver is { } receiver)
            {
                Add(replayed, receiver, (BigInteger)transaction.Amount + transaction.ReceiverRewards);
            }

            if (transaction.CloseTo is { } closeTo)
            {
                Add(replayed, closeTo, (BigInteger)transaction.ClosingAmount + transaction.CloseRewards);
            }
        }

        foreach (var (account, delta) in replayed.Where(kv => kv.Key != pool))
        {
            var atGenesis = await storage.GetBalanceAtRoundAsync(account, Native, 0, CancellationToken.None);
            var ours = await storage.GetBalanceAtRoundAsync(account, Native, Rounds, CancellationToken.None);
            Assert.Equal(atGenesis.Value + delta, ours.Value);
        }
    }

    private static void Add(Dictionary<Address, BigInteger> balances, Address account, BigInteger delta) =>
        balances[account] = balances.GetValueOrDefault(account) + delta;

    private static RoundIndexer CreateIndexer(IChainStorage storage, IChainAdapter adapter) =>
        new(
            adapter,
            new AlgoEffectNormalizer(),
            storage,
            Options.Create(new IndexerOptions { FetchConcurrency = 8, LogEveryRounds = 100 }),
            NullLogger<RoundIndexer>.Instance);

    private static CachedChainAdapter Adapter(string chain) =>
        Adapters.GetOrAdd(chain, c => new Lazy<CachedChainAdapter>(() =>
        {
            var http = AlgorandChainFactory.CreateBlockHttpClient();
            var options = new AlgorandChainOptions { Chain = c, MaxAttempts = 30, BaseRetryDelay = TimeSpan.FromMilliseconds(200), MaxRetryDelay = TimeSpan.FromSeconds(3) };
            var adapter = AlgorandChainFactory.Create(options, http, NullLoggerFactory.Instance);
            return new CachedChainAdapter(adapter);
        })).Value;

    private static ReferenceNode Reference(string chain) => chain switch
    {
        ChainId.AlgorandMainnet => ReferenceNode.MainNet(),
        ChainId.Voi => ReferenceNode.Voi(),
        _ => ReferenceNode.TestNet(),
    };

    private static Task<IReadOnlyDictionary<ulong, IReadOnlyDictionary<Address, ulong>>> LedgerAsync(string chain) =>
        Ledgers.GetOrAdd(chain, c => new Lazy<Task<IReadOnlyDictionary<ulong, IReadOnlyDictionary<Address, ulong>>>>(() => LoadLedgerAsync(c))).Value;

    private static async Task<IReadOnlyDictionary<ulong, IReadOnlyDictionary<Address, ulong>>> LoadLedgerAsync(string chain)
    {
        using var reference = Reference(chain);
        using var gate = new SemaphoreSlim(6);
        var rounds = Enumerable.Range(1, (int)Rounds).Select(static i => (ulong)i).ToArray();
        var loaded = await Task.WhenAll(rounds.Select(async round =>
        {
            await gate.WaitAsync();
            try
            {
                return (round, balances: await reference.LedgerBalancesAfterAsync(round));
            }
            finally
            {
                gate.Release();
            }
        }));

        return loaded.ToDictionary(static x => x.round, static x => x.balances);
    }

    private static async Task<List<Holding>> AllHoldersAsync(IChainStorage storage, ulong round)
    {
        var holders = new List<Holding>();
        string? cursor = null;
        do
        {
            var page = await storage.GetHoldersAtRoundAsync(Native, round, true, cursor, 500, CancellationToken.None);
            holders.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return holders;
    }

    private static async Task<List<TokenEffect>> AllEffectsAsync(IChainStorage storage, Address account)
    {
        var effects = new List<TokenEffect>();
        string? cursor = null;
        do
        {
            var page = await storage.GetEffectsAsync(account, Native, null, null, cursor, 500, CancellationToken.None);
            effects.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return effects;
    }
}

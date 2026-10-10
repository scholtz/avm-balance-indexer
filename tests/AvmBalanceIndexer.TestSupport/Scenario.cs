using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// A small deterministic chain history used by the storage contract suite. Expected balances are spelled out so every
/// provider can be checked against the same numbers.
/// </summary>
/// <remarks>
/// Genesis: Alice 1,000,000, Bob 10,000, fee sink 100, rewards pool 5,000,000. The rewards rate is 1,000 per round
/// and there are 1,500 reward units, so the pool is debited 1,500 in rounds 2, 3 and 5 and nothing in rounds 1 and 4.
/// Rounds:
/// 1: Alice pays Bob 3,000 (fee 1,000, rewards 10 to Alice).
/// 2: empty.
/// 3: Bob pays Carol 5,000 (fee 1,000).
/// 4: Carol pays Alice 100 (fee 1,000) and closes out to Dave (3,900), leaving Carol at zero.
/// 5: Alice pays Bob 1 (fee 2,000) and the call performs an inner payment Bob to Dave of 10.
/// </remarks>
public static class Scenario
{
    public static readonly Address Alice = TestChain.Account(1);
    public static readonly Address Bob = TestChain.Account(2);
    public static readonly Address Carol = TestChain.Account(3);
    public static readonly Address Dave = TestChain.Account(4);

    public const ulong LastRound = 5;

    public static readonly TestChain Chain = new();

    public static GenesisDocument Genesis { get; } = new(
        "v1.0",
        "testnet",
        [
            new GenesisAccount(Alice, 1_000_000, "Alice"),
            new GenesisAccount(Bob, 10_000, "Bob"),
            new GenesisAccount(Chain.FeeSink, 100, "FeeSink"),
            new GenesisAccount(Chain.Pool, 5_000_000, "RewardsPool"),
        ]);

    /// <summary>Rewards header state at the end of each round (rate 1,000; 1,500 reward units).</summary>
    public static RewardsState RewardsAt(ulong round) => round switch
    {
        0 => new RewardsState(0, 0, 1_000),
        1 => new RewardsState(0, 1_000, 1_000),
        2 => new RewardsState(1, 500, 1_000),
        3 => new RewardsState(2, 0, 1_000),
        4 => new RewardsState(2, 1_000, 1_000),
        _ => new RewardsState(3, 500, 1_000),
    };

    /// <summary>Total withdrawn from the rewards pool through the end of the round.</summary>
    public static ulong PoolWithdrawnThrough(ulong round) => round switch { < 2 => 0, 2 => 1_500, 3 or 4 => 3_000, _ => 4_500 };

    /// <summary>Total native supply at the end of <paramref name="round"/>, including realized rewards.</summary>
    public static ulong SupplyAt(ulong round) => 6_010_100UL + (round >= 1 ? 10UL : 0UL) - PoolWithdrawnThrough(round);

    public static IReadOnlyList<BlockData> Blocks() =>
    [
        Chain.Block(1, [new TestPayment(Alice, Bob, 3_000, Fee: 1_000, SenderRewards: 10)], rewards: RewardsAt(1)),
        Chain.Block(2, rewards: RewardsAt(2)),
        Chain.Block(3, [new TestPayment(Bob, Carol, 5_000, Fee: 1_000)], rewards: RewardsAt(3)),
        Chain.Block(4, [new TestPayment(Carol, Alice, 100, Fee: 1_000, CloseTo: Dave, ClosingAmount: 3_900)], rewards: RewardsAt(4)),
        Chain.Block(5, [new TestPayment(Alice, Bob, 1, Fee: 2_000, Inner: [new TestPayment(Bob, Dave, 10, Fee: 0)])], rewards: RewardsAt(5)),
    ];

    public static FakeChainAdapter Adapter(IEnumerable<BlockData>? blocks = null)
    {
        var adapter = new FakeChainAdapter(Chain.Profile, Genesis);
        foreach (var block in blocks ?? Blocks())
        {
            adapter.Add(block);
        }

        return adapter;
    }

    /// <summary>Expected end-of-round native balance per account: (round → balance).</summary>
    public static ulong ExpectedBalance(Address account, ulong round)
    {
        if (account == Alice)
        {
            return round switch { 0 => 1_000_000, 1 or 2 or 3 => 996_010, 4 => 996_110, _ => 994_109 };
        }

        if (account == Bob)
        {
            return round switch { 0 => 10_000, 1 or 2 => 13_000, 3 or 4 => 7_000, _ => 6_991 };
        }

        if (account == Carol)
        {
            return round switch { 3 => 5_000, _ => 0 };
        }

        if (account == Dave)
        {
            return round switch { < 4 => 0, 4 => 3_900, _ => 3_910 };
        }

        if (account == Chain.FeeSink)
        {
            return round switch { 0 => 100, 1 or 2 => 1_100, 3 => 2_100, 4 => 3_100, _ => 5_100 };
        }

        if (account == Chain.Pool)
        {
            return 5_000_000 - PoolWithdrawnThrough(round);
        }

        throw new ArgumentOutOfRangeException(nameof(account));
    }
}

using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Tests;

public class DomainModelTests
{
    private static Address AddressOf(byte value) => new(Enumerable.Repeat(value, Address.Length).ToArray());

    [Fact]
    public void Effect_Ids_Compare_By_Value_Including_The_Inner_Path()
    {
        var a = new EffectId(5, 1, "TX", [0, 1], 3);
        var same = new EffectId(5, 1, "TX", [0, 1], 3);
        var other = new EffectId(5, 1, "TX", [0, 2], 3);

        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void Addresses_Compare_By_Content_And_Reject_Wrong_Lengths()
    {
        Assert.Equal(AddressOf(1), AddressOf(1));
        Assert.NotEqual(AddressOf(1), AddressOf(2));
        Assert.True(AddressOf(1) == AddressOf(1));
        Assert.Throws<ArgumentException>(() => new Address(new byte[31]));
        Assert.Empty(default(Address).ToArray());
    }

    [Fact]
    public void Token_Effects_Carry_The_Delta_And_The_Running_Balance()
    {
        var delta = new BalanceDelta(new EffectId(1, 0, "TX", [], 0), AddressOf(1), TokenKey.Native, EffectType.Payment, new BigInteger(-5), AddressOf(2));

        var effect = TokenEffect.From(delta, Amount.From(95));

        Assert.Equal((delta.Id, delta.Account, delta.Delta, delta.Counterparty), (effect.Id, effect.Account, effect.Delta, effect.Counterparty));
        Assert.Equal(Amount.From(95), effect.BalanceAfter);
    }

    [Theory]
    [InlineData(0UL, 0)]
    [InlineData(99_999UL, 0)]
    [InlineData(100_000UL, 1)]
    [InlineData(68_144_556UL, 681)]
    public void Round_Buckets_Are_Fixed_Size(ulong round, int bucket)
    {
        Assert.Equal(bucket, RoundBuckets.ForRound(round));
    }

    [Theory]
    [InlineData(TokenKind.Algo, "algo")]
    [InlineData(TokenKind.Asa, "asa")]
    [InlineData(TokenKind.Arc200, "arc200")]
    public void Token_Kinds_Round_Trip_Through_Their_Storage_Names(TokenKind kind, string name)
    {
        Assert.Equal(name, kind.ToStorageName());
        Assert.True(TokenKindExtensions.TryParseStorageName(name, out var parsed));
        Assert.Equal(kind, parsed);
    }

    [Theory]
    [InlineData("ALGO")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_Storage_Names_Are_Rejected(string? name)
    {
        Assert.False(TokenKindExtensions.TryParseStorageName(name, out _));
    }

    [Fact]
    public void Chain_Status_Reports_Lag_Without_Underflow()
    {
        var behind = new ChainStatus("voi", 90, new byte[32], 100, IngestionState.Backfilling, DateTimeOffset.UnixEpoch);
        var ahead = behind with { TargetRound = 80 };
        var unknown = behind with { TargetRound = null };

        Assert.Equal((10UL, 0UL, 0UL), (behind.Lag, ahead.Lag, unknown.Lag));
    }

    [Fact]
    public void Divergence_And_Unindexed_Round_Errors_Describe_The_Problem()
    {
        var unindexed = new RoundNotIndexedException(10, 7);

        Assert.Contains("10", unindexed.Message, StringComparison.Ordinal);
        Assert.Contains("7", unindexed.Message, StringComparison.Ordinal);
        Assert.Equal("boom", new ChainDivergenceException("boom").Message);
    }
}

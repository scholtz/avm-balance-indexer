using System.Numerics;
using Algorand.Algod.Model;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>The rewards fields of a block header that drive the per-round rewards pool withdrawal.</summary>
/// <param name="Level">Rewards distributed per reward unit since genesis (header field <c>earn</c>).</param>
/// <param name="Residue">Leftover base units after the last distribution (header field <c>frac</c>).</param>
/// <param name="Rate">Base units added to the participation stake per round (header field <c>rate</c>).</param>
public readonly record struct RewardsState(ulong Level, ulong Residue, ulong Rate)
{
    /// <summary>Protocols before v31 keep using the previous rate in the round the rate is recalculated.</summary>
    private static readonly HashSet<string> ProtocolsWithoutImmediateRate = new(StringComparer.Ordinal)
    {
        "v0", "v1", "v2", "v3", "v4", "v5", "v6", "v7", "v8", "v9", "v10", "v11", "v12",
        "https://github.com/algorand/spec/tree/0c8a9dc44d7368cc266d5407b79fb3311f4fc795",
        "https://github.com/algorand/spec/tree/2526b6ae062b4fe5e163e06e41e1d9b9219135a9",
        "https://github.com/algorand/spec/tree/a26ed78ed8f834e2b9ccb6eb7d3ee9f629a6e622",
        "https://github.com/algorand/spec/tree/22726c9dcd12d9cddce4a8bd7e8ccaa707f74101",
        "https://github.com/algorandfoundation/specs/tree/5615adc36bad610c7f165fa2967f4ecfa75125f0",
        "https://github.com/algorandfoundation/specs/tree/6c6bd668be0ab14098e51b37e806c509f7b7e31f",
        "https://github.com/algorandfoundation/specs/tree/0e196e82bfd6e327994bec373c4cc81bc878ef5c",
        "https://github.com/algorandfoundation/specs/tree/4a9db6a25595c6fd097cf9cc137cc83027787eaa",
        "https://github.com/algorandfoundation/specs/tree/8096e2df2da75c3339986317f9abe69d4fa86b4b",
        "https://github.com/algorandfoundation/specs/tree/57016b942f6d97e6d4c0688b373bb0a2fc85a1a2",
        "https://github.com/algorandfoundation/specs/tree/e5f565421d720c6f75cdd186f7098495caf9101f",
        "https://github.com/algorandfoundation/specs/tree/3a83c4c743f8b17adfd73944b4319c25722a6782",
        "https://github.com/algorandfoundation/specs/tree/bea19289bf41217d2c0af30522fa222ef1366466",
        "https://github.com/algorandfoundation/specs/tree/ac2255d586c4474d4ebcf3809acccb59b7ef34ff",
        "https://github.com/algorandfoundation/specs/tree/d050b3cade6d5c664df8bd729bf219f179812595",
        "https://github.com/algorandfoundation/specs/tree/65b4ab3266c52c56a0fa7d591754887d68faad0a",
        "https://github.com/algorandfoundation/specs/tree/abc54f79f9ad679d2d22f0fb9909fb005c16f8a1",
        "https://github.com/algorandfoundation/specs/tree/bc36005dbd776e6d1eaf0c560619bb183215645c",
    };

    /// <summary>Reads the rewards state from a block header; absent fields are zero.</summary>
    public static RewardsState FromHeader(Block header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return new RewardsState(header.RewardsLevel ?? 0, header.RewardsResidue ?? 0, header.RewardsRate ?? 0);
    }

    /// <summary>
    /// Amount the ledger withdraws from the rewards pool at the start of a block. The ledger debits the pool by
    /// <c>rewardUnits × (level − previous level)</c>. Because the new level and residue are computed as
    /// <c>level + (rate + residue) / units</c> and <c>(rate + residue) % units</c>, that product equals
    /// <c>rate + previous residue − residue</c>, which needs nothing but the two headers.
    /// </summary>
    /// <param name="previous">Rewards state of the previous block.</param>
    /// <param name="current">Rewards state of the block being processed.</param>
    /// <param name="protocol">Consensus protocol of the block being processed.</param>
    public static BigInteger PoolWithdrawal(RewardsState previous, RewardsState current, string? protocol)
    {
        if (current.Level == previous.Level && current.Residue == previous.Residue)
        {
            // Either nothing is distributed (no reward units, or a zero rate) or the state did not move.
            return BigInteger.Zero;
        }

        var rate = protocol is not null && ProtocolsWithoutImmediateRate.Contains(protocol) ? previous.Rate : current.Rate;
        var withdrawal = new BigInteger(rate) + previous.Residue - current.Residue;
        return withdrawal.Sign < 0
            ? throw new InvalidDataException($"Rewards state moved from {previous} to {current} with a negative pool withdrawal.")
            : withdrawal;
    }
}

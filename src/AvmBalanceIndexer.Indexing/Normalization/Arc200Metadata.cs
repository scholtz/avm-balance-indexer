using System.Numerics;
using System.Text;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>
/// What the read-only methods of an ARC-200 application answer (ARC-0200: <c>arc200_name</c>, <c>arc200_symbol</c>, <c>arc200_decimals</c>,
/// <c>arc200_totalSupply</c>). A null member means the application did not answer that method with a well-formed value.
/// </summary>
/// <param name="Name">The token name, at most 32 bytes of UTF-8.</param>
/// <param name="Symbol">The token symbol, at most 8 bytes of UTF-8.</param>
/// <param name="Decimals">The number of decimals (0 to 255).</param>
/// <param name="TotalSupply">The total supply at the time of the lookup.</param>
public sealed record Arc200Metadata(string? Name, string? Symbol, int? Decimals, BigInteger? TotalSupply)
{
    private static readonly byte[] ReturnPrefix = [0x15, 0x1f, 0x7c, 0x75];

    /// <summary>The four method signatures in the order name, symbol, decimals, totalSupply.</summary>
    public static IReadOnlyList<string> Signatures { get; } =
        ["arc200_name()byte[32]", "arc200_symbol()byte[8]", "arc200_decimals()uint8", "arc200_totalSupply()uint256"];

    /// <summary>True when all four methods answered with well-formed values (the standard is then considered verified).</summary>
    public bool Complete => Name is not null && Symbol is not null && Decimals is not null && TotalSupply is not null;

    /// <summary>The 4-byte ABI selector of a method signature.</summary>
    /// <param name="signature">For example <c>arc200_decimals()uint8</c>.</param>
    public static byte[] Selector(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return Sha512t256.Hash(Encoding.UTF8.GetBytes(signature))[..4];
    }

    /// <summary>Decodes the last log of a call to a <c>byte[N]</c> method: the return prefix, then N bytes of UTF-8 padded with zeros on the right.</summary>
    /// <param name="abiReturn">The log.</param>
    /// <param name="size">The N of <c>byte[N]</c>.</param>
    /// <param name="text">The text without the padding.</param>
    public static bool TryDecodeText(ReadOnlySpan<byte> abiReturn, int size, out string text)
    {
        text = string.Empty;
        if (abiReturn.Length != ReturnPrefix.Length + size || !abiReturn[..ReturnPrefix.Length].SequenceEqual(ReturnPrefix))
        {
            return false;
        }

        var value = abiReturn[ReturnPrefix.Length..].TrimEnd((byte)0);
        if (value.IsEmpty)
        {
            return false;
        }

        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Decodes the last log of a call to a <c>uintN</c> method: the return prefix, then N/8 bytes big-endian.</summary>
    /// <param name="abiReturn">The log.</param>
    /// <param name="size">The number of bytes of the integer (1 for uint8, 32 for uint256).</param>
    /// <param name="value">The unsigned value.</param>
    public static bool TryDecodeUint(ReadOnlySpan<byte> abiReturn, int size, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (abiReturn.Length != ReturnPrefix.Length + size || !abiReturn[..ReturnPrefix.Length].SequenceEqual(ReturnPrefix))
        {
            return false;
        }

        value = new BigInteger(abiReturn[ReturnPrefix.Length..], isUnsigned: true, isBigEndian: true);
        return true;
    }
}

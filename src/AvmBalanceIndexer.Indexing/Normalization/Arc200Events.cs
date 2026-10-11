using System.Numerics;
using System.Text;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>
/// Decoder for the ARC-200 transfer event: an ARC-28 style log made of the 4-byte selector of
/// <c>arc200_Transfer(address,address,uint256)</c> followed by the sender, the receiver (32 bytes each) and the amount
/// (uint256, big endian). Logs of any other shape are not transfers.
/// </summary>
public static class Arc200Events
{
    /// <summary>Text the selector is derived from.</summary>
    public const string Signature = "arc200_Transfer(address,address,uint256)";

    private const int SelectorLength = 4;
    private const int AddressLength = 32;
    private const int AmountLength = 32;
    private const int LogLength = SelectorLength + (2 * AddressLength) + AmountLength;

    private static readonly byte[] Selector = Sha512t256.Hash(Encoding.UTF8.GetBytes(Signature))[..SelectorLength];

    /// <summary>The 4-byte selector at the start of every transfer log.</summary>
    public static ReadOnlySpan<byte> TransferSelector => Selector;

    /// <summary>Decodes a log as an ARC-200 transfer.</summary>
    /// <returns>False when the log is not a transfer event.</returns>
    public static bool TryParseTransfer(ReadOnlySpan<byte> log, out byte[] from, out byte[] to, out BigInteger amount)
    {
        from = [];
        to = [];
        amount = BigInteger.Zero;
        if (log.Length != LogLength || !log[..SelectorLength].SequenceEqual(Selector))
        {
            return false;
        }

        from = log.Slice(SelectorLength, AddressLength).ToArray();
        to = log.Slice(SelectorLength + AddressLength, AddressLength).ToArray();
        amount = new BigInteger(log.Slice(SelectorLength + (2 * AddressLength), AmountLength), isUnsigned: true, isBigEndian: true);
        return true;
    }
}

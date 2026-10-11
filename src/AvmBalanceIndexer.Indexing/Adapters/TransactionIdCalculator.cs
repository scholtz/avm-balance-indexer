using System.Buffers;
using MessagePack;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Computes transaction ids from the canonical msgpack stored in a block. Blocks strip the genesis hash and id from
/// transactions (flags hgh and hgi); they are re-inserted in canonical (sorted key) position before hashing,
/// which is exact for every transaction type without relying on SDK model coverage.
/// </summary>
public static class TransactionIdCalculator
{
    /// <summary>Returns the base32 transaction id of a top-level block transaction.</summary>
    public static string Compute(TxnApplyData apply, ReadOnlySpan<byte> genesisHash, string genesisId) =>
        Base32.Encode(ComputeDigest(apply, genesisHash, genesisId, sha256: false));

    /// <summary>
    /// Returns the raw 32-byte transaction digest: SHA-512/256 (the transaction id) or, for the SHA-256 payset vector
    /// commitment, SHA-256 over the same <c>"TX"</c> prefixed canonical encoding.
    /// </summary>
    public static byte[] ComputeDigest(TxnApplyData apply, ReadOnlySpan<byte> genesisHash, string genesisId, bool sha256)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (apply.RawTxn.IsEmpty)
        {
            throw new InvalidDataException("Transaction has no raw txn map.");
        }

        var canonical = Canonicalize(apply, genesisHash.ToArray(), genesisId);
        var input = new byte[2 + canonical.Length];
        "TX"u8.CopyTo(input);
        canonical.CopyTo(input, 2);
        if (sha256)
        {
            return System.Security.Cryptography.SHA256.HashData(input);
        }

        return Sha512t256.Hash(input);
    }

    private static byte[] Canonicalize(TxnApplyData apply, byte[] genesisHash, string genesisId)
    {
        var entries = new List<(string Key, byte[] Value)>();
        var reader = new MessagePackReader(apply.RawTxn);
        var count = reader.ReadMapHeader();
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadString() ?? throw new InvalidDataException("Transaction map has a non-string key.");
            var start = reader.Position;
            reader.Skip();
            entries.Add((key, reader.Sequence.Slice(start, reader.Position).ToArray()));
        }

        if (apply.HasGenesisHash && entries.TrueForAll(static e => e.Key != "gh"))
        {
            entries.Add(("gh", EncodeBinary(genesisHash)));
        }

        if (apply.HasGenesisId && entries.TrueForAll(static e => e.Key != "gen"))
        {
            entries.Add(("gen", EncodeString(genesisId)));
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(entries.Count);
        foreach (var (key, value) in entries)
        {
            writer.Write(key);
            writer.WriteRaw(value);
        }

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeBinary(byte[] value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.Write(value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeString(string value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.Write(value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }
}

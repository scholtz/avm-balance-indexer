using System.Buffers;
using MessagePack;
using Org.BouncyCastle.Crypto.Digests;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Computes transaction ids from the canonical msgpack stored in a block. Blocks strip the genesis hash and id from
/// transactions (flags hgh and hgi); they are re-inserted in canonical (sorted key) position before hashing,
/// which is exact for every transaction type without relying on SDK model coverage.
/// </summary>
public static class TransactionIdCalculator
{
    /// <summary>Returns the base32 transaction id of a top-level block transaction.</summary>
    public static string Compute(TxnApplyData apply, ReadOnlySpan<byte> genesisHash, string genesisId)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (apply.RawTxn.IsEmpty)
        {
            throw new InvalidDataException("Transaction has no raw txn map.");
        }

        var canonical = Canonicalize(apply, genesisHash.ToArray(), genesisId);
        var digest = new Sha512tDigest(256);
        digest.BlockUpdate("TX"u8);
        digest.BlockUpdate(canonical, 0, canonical.Length);
        var hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        return Base32.Encode(hash);
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

        if (apply.HasGenesisHash)
        {
            entries.Add(("gh", EncodeBinary(genesisHash)));
        }

        if (apply.HasGenesisId)
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

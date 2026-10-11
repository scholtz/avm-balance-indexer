using System.Buffers;
using MessagePack;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Reads the apply data of every transaction in a gossip block response with a forward-only msgpack walk.
/// Only the fields needed for balance accounting are materialized; everything else is skipped.
/// </summary>
public static class ApplyDataReader
{
    /// <summary>Returns the apply data of all top-level transactions of the block in block order.</summary>
    public static IReadOnlyList<TxnApplyData> ReadBlockTransactions(ReadOnlyMemory<byte> payload)
    {
        var reader = new MessagePackReader(payload);
        IReadOnlyList<TxnApplyData> result = [];

        var rootEntries = reader.ReadMapHeader();
        for (var i = 0; i < rootEntries; i++)
        {
            if (reader.ReadString() == "block")
            {
                result = ReadBlock(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        return result;
    }

    private static IReadOnlyList<TxnApplyData> ReadBlock(ref MessagePackReader reader)
    {
        IReadOnlyList<TxnApplyData> result = [];
        var entries = reader.ReadMapHeader();
        for (var i = 0; i < entries; i++)
        {
            if (reader.ReadString() == "txns")
            {
                result = ReadTransactions(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        return result;
    }

    private static List<TxnApplyData> ReadTransactions(ref MessagePackReader reader)
    {
        var result = new List<TxnApplyData>();
        if (reader.TryReadNil())
        {
            return result;
        }

        var count = reader.ReadArrayHeader();
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadTransaction(ref reader));
        }

        return result;
    }

    private static TxnApplyData ReadTransaction(ref MessagePackReader reader)
    {
        ReadOnlyMemory<byte> rawTxn = default;
        ulong rs = 0, rr = 0, rc = 0, ca = 0, aca = 0;
        bool hgh = false, hgi = false;
        IReadOnlyList<TxnApplyData> inner = [];

        var entries = reader.ReadMapHeader();
        for (var i = 0; i < entries; i++)
        {
            switch (reader.ReadString())
            {
                case "txn":
                    var start = reader.Position;
                    reader.Skip();
                    rawTxn = reader.Sequence.Slice(start, reader.Position).ToArray();
                    break;
                case "rs":
                    rs = reader.ReadUInt64();
                    break;
                case "rr":
                    rr = reader.ReadUInt64();
                    break;
                case "rc":
                    rc = reader.ReadUInt64();
                    break;
                case "ca":
                    ca = reader.ReadUInt64();
                    break;
                case "aca":
                    aca = reader.ReadUInt64();
                    break;
                case "hgh":
                    hgh = reader.ReadBoolean();
                    break;
                case "hgi":
                    hgi = reader.ReadBoolean();
                    break;
                case "dt":
                    inner = ReadDetail(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new TxnApplyData(rawTxn, rs, rr, rc, ca, aca, hgh, hgi, inner);
    }

    private static IReadOnlyList<TxnApplyData> ReadDetail(ref MessagePackReader reader)
    {
        IReadOnlyList<TxnApplyData> inner = [];
        var entries = reader.ReadMapHeader();
        for (var i = 0; i < entries; i++)
        {
            if (reader.ReadString() == "itx")
            {
                inner = ReadTransactions(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        return inner;
    }
}

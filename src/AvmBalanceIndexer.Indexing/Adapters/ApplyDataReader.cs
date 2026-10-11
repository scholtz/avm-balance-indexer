using System.Buffers;
using MessagePack;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Reads the apply data of every transaction in a gossip block response with a forward-only msgpack walk.
/// Only the fields needed for balance accounting are materialized; everything else is skipped.
/// </summary>
public static class ApplyDataReader
{
    // The only logs kept are ARC-200 transfer events (see Arc200Events); every other log is skipped without copying it.
    private const int Arc200TransferLogLength = 100;

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
        ulong rs = 0, rr = 0, rc = 0, ca = 0, aca = 0, caid = 0, apid = 0;
        bool hgh = false, hgi = false;
        IReadOnlyList<TxnApplyData> inner = [];
        IReadOnlyList<ReadOnlyMemory<byte>> logs = [];

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
                case "caid":
                    caid = reader.ReadUInt64();
                    break;
                case "apid":
                    apid = reader.ReadUInt64();
                    break;
                case "hgh":
                    hgh = reader.ReadBoolean();
                    break;
                case "hgi":
                    hgi = reader.ReadBoolean();
                    break;
                case "dt":
                    (inner, logs) = ReadDetail(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new TxnApplyData(rawTxn, rs, rr, rc, ca, aca, caid, hgh, hgi, inner, apid, logs);
    }

    private static (IReadOnlyList<TxnApplyData> Inner, IReadOnlyList<ReadOnlyMemory<byte>> Logs) ReadDetail(ref MessagePackReader reader)
    {
        IReadOnlyList<TxnApplyData> inner = [];
        IReadOnlyList<ReadOnlyMemory<byte>> logs = [];
        var entries = reader.ReadMapHeader();
        for (var i = 0; i < entries; i++)
        {
            switch (reader.ReadString())
            {
                case "itx":
                    inner = ReadTransactions(ref reader);
                    break;
                case "lg":
                    logs = ReadLogs(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return (inner, logs);
    }

    private static List<ReadOnlyMemory<byte>> ReadLogs(ref MessagePackReader reader)
    {
        var logs = new List<ReadOnlyMemory<byte>>();
        if (reader.TryReadNil())
        {
            return logs;
        }

        var count = reader.ReadArrayHeader();
        for (var i = 0; i < count; i++)
        {
            // Only 100-byte logs can be ARC-200 transfers; keeping the others would allocate for nothing.
            if (reader.ReadBytes() is { Length: Arc200TransferLogLength } log)
            {
                logs.Add(log.ToArray());
            }
        }

        return logs;
    }
}

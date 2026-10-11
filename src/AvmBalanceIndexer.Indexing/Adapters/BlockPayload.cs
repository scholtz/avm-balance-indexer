using System.Buffers;
using MessagePack;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Splits a gossip block response. The payset (<c>txns</c>) is read straight from the msgpack by the apply data reader; the
/// SDK model is only used for the header and the certificate. That keeps the SDK out of the transactions, which it cannot
/// decode in every case (a LogicSig with a very large argument, for example, makes it throw) and which is the expensive part.
/// </summary>
public static class BlockPayload
{
    /// <summary>Returns the response with the <c>txns</c> entry removed from its <c>block</c> map.</summary>
    /// <exception cref="InvalidDataException">The payload has no block.</exception>
    public static byte[] WithoutTransactions(ReadOnlyMemory<byte> payload)
    {
        var reader = new MessagePackReader(payload);
        var buffer = new ArrayBufferWriter<byte>(Math.Clamp(payload.Length, 256, 4096));
        var writer = new MessagePackWriter(buffer);
        var found = false;

        var rootEntries = reader.ReadMapHeader();
        writer.WriteMapHeader(rootEntries);
        for (var i = 0; i < rootEntries; i++)
        {
            var key = reader.ReadString();
            writer.Write(key);
            if (key != "block")
            {
                var start = reader.Position;
                reader.Skip();
                writer.WriteRaw(reader.Sequence.Slice(start, reader.Position));
                continue;
            }

            found = true;
            var entries = new List<ReadOnlySequence<byte>>();
            var count = reader.ReadMapHeader();
            for (var j = 0; j < count; j++)
            {
                var start = reader.Position;
                var name = reader.ReadString();
                reader.Skip();
                if (name != "txns")
                {
                    entries.Add(reader.Sequence.Slice(start, reader.Position));
                }
            }

            writer.WriteMapHeader(entries.Count);
            foreach (var entry in entries)
            {
                writer.WriteRaw(entry);
            }
        }

        writer.Flush();
        return found ? buffer.WrittenSpan.ToArray() : throw new InvalidDataException("The payload has no block.");
    }
}

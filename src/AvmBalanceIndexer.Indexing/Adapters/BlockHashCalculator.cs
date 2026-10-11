using System.Buffers;
using MessagePack;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Computes the block hash the way the ledger does: SHA-512/256 over <c>"BH"</c> and the canonical msgpack of the block
/// header. A stored block is the header fields plus the payset (<c>txns</c>) in one map with sorted keys, so the header
/// encoding is that map without <c>txns</c>.
/// </summary>
public static class BlockHashCalculator
{
    /// <summary>Returns the 32-byte block hash of the <c>block</c> inside a gossip block response.</summary>
    /// <exception cref="InvalidDataException">The payload has no block.</exception>
    public static byte[] Compute(ReadOnlyMemory<byte> payload)
    {
        var reader = new MessagePackReader(payload);
        var rootEntries = reader.ReadMapHeader();
        for (var i = 0; i < rootEntries; i++)
        {
            if (reader.ReadString() == "block")
            {
                return HashHeader(ref reader);
            }

            reader.Skip();
        }

        throw new InvalidDataException("The payload has no block.");
    }

    private static byte[] HashHeader(ref MessagePackReader reader)
    {
        var entries = new List<byte[]>();
        var count = reader.ReadMapHeader();
        for (var i = 0; i < count; i++)
        {
            var start = reader.Position;
            var key = reader.ReadString();
            reader.Skip();
            if (key != "txns")
            {
                entries.Add(reader.Sequence.Slice(start, reader.Position).ToArray());
            }
        }

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(entries.Count);
        foreach (var entry in entries)
        {
            writer.WriteRaw(entry);
        }

        writer.Flush();

        var input = new byte[2 + buffer.WrittenCount];
        "BH"u8.CopyTo(input);
        buffer.WrittenSpan.CopyTo(input.AsSpan(2));
        return Sha512t256.Hash(input);
    }
}

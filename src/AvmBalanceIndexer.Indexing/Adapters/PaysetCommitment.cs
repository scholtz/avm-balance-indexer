using System.Buffers;
using System.Numerics;
using System.Security.Cryptography;
using MessagePack;
using Org.BouncyCastle.Crypto.Digests;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>
/// Recomputes the transaction commitments of a block header (<c>txn</c>, <c>txn256</c>, <c>txn512</c>) from the served
/// transactions, as go-algorand's <c>Block.PaysetCommit</c> does, so that a host cannot alter or drop transactions of a
/// block whose header hash is certified.
/// <list type="bullet">
/// <item><c>txn</c> depends on the protocol: none before v11, a flat hash of the payset for v11 to v25, a SHA-512/256 Merkle
/// tree from v26 (see <see cref="ConsensusProtocols"/>; unknown protocols are treated as the newest scheme).</item>
/// <item><c>txn256</c> and <c>txn512</c> are vector commitments (power of two padded, bit-reversed leaf order) that exist when the
/// header carries them. The header is authenticated by the block hash, so its presence decides.</item>
/// </list>
/// </summary>
public static class PaysetCommitment
{

    private enum HashKind
    {
        Sha512_256,
        Sha256,
        Sha512,
    }

    /// <summary>Checks the header commitments of a gossip block response against its transactions.</summary>
    /// <exception cref="BlockValidationException">A commitment does not match.</exception>
    public static void Verify(byte[] payload, IReadOnlyList<TxnApplyData> apply, byte[] genesisHash, string genesisId, ulong round)
    {
        var (header, stibs) = Read(payload);
        var protocol = header.Protocol ?? string.Empty;

        if (apply.Count != stibs.Count)
        {
            throw new BlockValidationException($"Round {round}: {stibs.Count} payset entries but {apply.Count} apply entries.");
        }

        var txids = new byte[apply.Count][];
        for (var i = 0; i < apply.Count; i++)
        {
            txids[i] = TransactionIdCalculator.ComputeDigest(apply[i], genesisHash, genesisId, sha256: false);
        }

        var stibHashes = stibs.Select(static s => Hash(HashKind.Sha512_256, "STIB"u8, s)).ToArray();

        var expected = ExpectedTxn(protocol, stibs, txids, stibHashes);
        Check(round, stibs.Count, protocol, "txn", header.Txn ?? new byte[32], expected);

        if (header.Txn256 is not null || header.Txn512 is not null)
        {
            // go-algorand's leaf uses the SHA-256 transaction id and payset entry hash for every tree but the SHA-512/256 one,
            // so the SHA-512 tree hashes those 32-byte values with SHA-512.
            var ids256 = apply.Select(a => TransactionIdCalculator.ComputeDigest(a, genesisHash, genesisId, sha256: true)).ToArray();
            var stib256 = stibs.Select(static s => Hash(HashKind.Sha256, "STIB"u8, s)).ToArray();
            if (header.Txn256 is { } txn256)
            {
                Check(round, stibs.Count, protocol, "txn256", txn256, MerkleRoot(HashKind.Sha256, ids256, stib256, vector: true));
            }

            if (header.Txn512 is { } txn512)
            {
                Check(round, stibs.Count, protocol, "txn512", txn512, MerkleRoot(HashKind.Sha512, ids256, stib256, vector: true));
            }
        }
    }

    /// <summary>The <c>txn</c> header commitment the transactions of <paramref name="payload"/> must have.</summary>
    public static byte[] ComputeTxn(byte[] payload, IReadOnlyList<TxnApplyData> apply, byte[] genesisHash, string genesisId)
    {
        var (header, stibs) = Read(payload);
        var txids = apply.Select(a => TransactionIdCalculator.ComputeDigest(a, genesisHash, genesisId, sha256: false)).ToArray();
        var stibHashes = stibs.Select(static s => Hash(HashKind.Sha512_256, "STIB"u8, s)).ToArray();
        return ExpectedTxn(header.Protocol ?? string.Empty, stibs, txids, stibHashes);
    }

    private static byte[] ExpectedTxn(string protocol, List<byte[]> stibs, byte[][] txids, byte[][] stibHashes) =>
        ConsensusProtocols.HasNoPaysetCommitment(protocol)
            ? new byte[32]
            : ConsensusProtocols.UsesFlatPaysetCommitment(protocol) ? FlatCommit(stibs) : MerkleRoot(HashKind.Sha512_256, txids, stibHashes, vector: false);

    private static void Check(ulong round, int count, string protocol, string field, byte[] actual, byte[] expected)
    {
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new BlockValidationException($"Round {round}: the {count} transactions do not match the header commitment {field} (protocol {protocol}).");
        }
    }

    private static byte[] FlatCommit(List<byte[]> stibs)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        if (stibs.Count == 0)
        {
            writer.WriteNil();
        }
        else
        {
            writer.WriteArrayHeader(stibs.Count);
            foreach (var stib in stibs)
            {
                writer.WriteRaw(stib);
            }
        }

        writer.Flush();
        return Hash(HashKind.Sha512_256, "PF"u8, buffer.WrittenSpan.ToArray());
    }

    private static byte[] MerkleRoot(HashKind kind, byte[][] txids, byte[][] stibHashes, bool vector)
    {
        var count = txids.Length;
        byte[] Leaf(int index) => Hash(kind, "TL"u8, [.. txids[index], .. stibHashes[index]]);

        List<byte[]> layer;
        if (vector)
        {
            // go-algorand pads to a power of two, but an array of zero or one element is left at a single leaf.
            var path = count <= 1 ? 0 : BitOperations.Log2((uint)(count - 1)) + 1;
            var size = 1 << path;
            layer = new List<byte[]>(size);
            for (var position = 0; position < size; position++)
            {
                var index = path == 0 ? 0 : (int)(ReverseBits((uint)position) >> (32 - path));
                layer.Add(index < count ? Leaf(index) : Hash(kind, "MB"u8, []));
            }
        }
        else
        {
            if (count == 0)
            {
                return new byte[32];
            }

            layer = Enumerable.Range(0, count).Select(Leaf).ToList();
        }

        var digestSize = layer[0].Length;
        while (layer.Count > 1)
        {
            var next = new List<byte[]>((layer.Count + 1) / 2);
            for (var i = 0; i < layer.Count; i += 2)
            {
                var pair = new byte[2 * digestSize];
                layer[i].CopyTo(pair, 0);
                if (i + 1 < layer.Count)
                {
                    layer[i + 1].CopyTo(pair, digestSize);
                }

                next.Add(Hash(kind, "MA"u8, pair));
            }

            layer = next;
        }

        return layer[0];
    }

    private static uint ReverseBits(uint value)
    {
        uint result = 0;
        for (var i = 0; i < 32; i++)
        {
            result = (result << 1) | ((value >> i) & 1);
        }

        return result;
    }

    private static byte[] Hash(HashKind kind, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> data)
    {
        var input = new byte[prefix.Length + data.Length];
        prefix.CopyTo(input);
        data.CopyTo(input.AsSpan(prefix.Length));
        switch (kind)
        {
            case HashKind.Sha256:
                return SHA256.HashData(input);
            case HashKind.Sha512:
                return SHA512.HashData(input);
            default:
                var digest = new Sha512tDigest(256);
                digest.BlockUpdate(input, 0, input.Length);
                var result = new byte[digest.GetDigestSize()];
                digest.DoFinal(result, 0);
                return result;
        }
    }

    private sealed record HeaderCommitments(string? Protocol, byte[]? Txn, byte[]? Txn256, byte[]? Txn512);

    private static (HeaderCommitments Header, List<byte[]> Stibs) Read(byte[] payload)
    {
        var reader = new MessagePackReader(payload);
        var root = reader.ReadMapHeader();
        for (var i = 0; i < root; i++)
        {
            if (reader.ReadString() != "block")
            {
                reader.Skip();
                continue;
            }

            string? protocol = null;
            byte[]? txn = null, txn256 = null, txn512 = null;
            var stibs = new List<byte[]>();
            var entries = reader.ReadMapHeader();
            for (var j = 0; j < entries; j++)
            {
                switch (reader.ReadString())
                {
                    case "proto":
                        protocol = reader.ReadString();
                        break;
                    case "txn":
                        txn = reader.ReadBytes()?.ToArray();
                        break;
                    case "txn256":
                        txn256 = reader.ReadBytes()?.ToArray();
                        break;
                    case "txn512":
                        txn512 = reader.ReadBytes()?.ToArray();
                        break;
                    case "txns":
                        if (reader.TryReadNil())
                        {
                            break;
                        }

                        var count = reader.ReadArrayHeader();
                        for (var k = 0; k < count; k++)
                        {
                            var start = reader.Position;
                            reader.Skip();
                            stibs.Add(reader.Sequence.Slice(start, reader.Position).ToArray());
                        }

                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return (new HeaderCommitments(protocol, txn, txn256, txn512), stibs);
        }

        throw new InvalidDataException("The payload has no block.");
    }
}

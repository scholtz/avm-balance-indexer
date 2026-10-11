using Org.BouncyCastle.Crypto.Digests;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>SHA-512/256, the hash Algorand uses for ids, block hashes and commitments.</summary>
internal static class Sha512t256
{
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var digest = new Sha512tDigest(256);
        digest.BlockUpdate(data);
        var result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return result;
    }
}

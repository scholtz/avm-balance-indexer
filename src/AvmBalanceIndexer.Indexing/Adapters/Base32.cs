namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>RFC 4648 base32 without padding, the text form of Algorand transaction ids and digests.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var chars = new char[((data.Length * 8) + 4) / 5];
        int buffer = 0, bits = 0, index = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                chars[index++] = Alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            chars[index++] = Alphabet[(buffer << (5 - bits)) & 31];
        }

        return new string(chars, 0, index);
    }
}

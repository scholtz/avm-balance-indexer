namespace AvmBalanceIndexer.Domain.Chains;

/// <summary>
/// A 32-byte AVM account public key. Stored as raw bytes in both providers and rendered as a
/// base32 Algorand address (with checksum) only at the API boundary.
/// </summary>
public readonly struct Address : IEquatable<Address>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    public Address(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"Address must be exactly {Length} bytes.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public ReadOnlySpan<byte> Bytes => _bytes ?? [];

    public byte[] ToArray() => Bytes.ToArray();

    public bool Equals(Address other) => Bytes.SequenceEqual(other.Bytes);

    public override bool Equals(object? obj) => obj is Address other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes);
        return hash.ToHashCode();
    }

    public static bool operator ==(Address left, Address right) => left.Equals(right);

    public static bool operator !=(Address left, Address right) => !left.Equals(right);

    public override string ToString() => Convert.ToHexStringLower(Bytes);
}

using System.Globalization;
using System.Numerics;

namespace AvmBalanceIndexer.Domain.Tokens;

/// <summary>
/// Exact, non-negative token amount in base units. Backed by <see cref="BigInteger"/> so that
/// uint64 values never overflow signed 64-bit arithmetic. Serialised as a canonical decimal string.
/// </summary>
public readonly record struct Amount : IComparable<Amount>
{
    public static readonly Amount Zero = new(BigInteger.Zero);

    /// <summary>Largest value representable on-ledger (uint64 max).</summary>
    public static readonly Amount MaxValue = new(new BigInteger(ulong.MaxValue));

    public BigInteger Value { get; }

    public Amount(BigInteger value)
    {
        if (value.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Amount cannot be negative.");
        }

        Value = value;
    }

    public static Amount From(ulong value) => new(new BigInteger(value));

    /// <summary>Parses a canonical decimal string (digits only, no sign).</summary>
    public static Amount Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"Value '{text}' is not a canonical decimal amount.");
        }

        return new Amount(value);
    }

    public Amount Add(Amount other) => new(Value + other.Value);

    /// <summary>Subtracts <paramref name="other"/>; throws if the result would be negative.</summary>
    public Amount Subtract(Amount other)
    {
        var result = Value - other.Value;
        return result.Sign < 0
            ? throw new InvalidOperationException($"Balance underflow: {Value} - {other.Value}.")
            : new Amount(result);
    }

    /// <summary>Applies a signed delta; throws if the result would be negative.</summary>
    public Amount Apply(BigInteger delta)
    {
        var result = Value + delta;
        return result.Sign < 0
            ? throw new InvalidOperationException($"Balance underflow: {Value} + ({delta}).")
            : new Amount(result);
    }

    public bool IsZero => Value.IsZero;

    public int CompareTo(Amount other) => Value.CompareTo(other.Value);

    public static bool operator <(Amount left, Amount right) => left.CompareTo(right) < 0;

    public static bool operator >(Amount left, Amount right) => left.CompareTo(right) > 0;

    public static bool operator <=(Amount left, Amount right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Amount left, Amount right) => left.CompareTo(right) >= 0;

    /// <summary>Canonical decimal representation used by both storage providers and the API.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

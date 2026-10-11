using AvmBalanceIndexer.Domain.Chains;

namespace AvmBalanceIndexer.Api;

/// <summary>The base32 text form (58 characters with checksum) of an <see cref="Address"/>, used only at the API boundary.</summary>
public static class AddressText
{
    public static string Format(Address address) => new global::Algorand.Address(address.ToArray()).EncodeAsString();

    public static bool TryParse(string? text, out Address address)
    {
        address = default;
        if (text is not { Length: 58 })
        {
            return false;
        }

        try
        {
            address = new Address(new global::Algorand.Address(text).Bytes);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }
    }
}

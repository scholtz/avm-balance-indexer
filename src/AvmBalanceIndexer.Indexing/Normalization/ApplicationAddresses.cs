using System.Buffers.Binary;
using System.Text;
using AvmBalanceIndexer.Indexing.Adapters;
using DomainAddress = AvmBalanceIndexer.Domain.Chains.Address;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>The account an application controls: SHA-512/256 of <c>"appID"</c> and the big-endian application id.</summary>
public static class ApplicationAddresses
{
    private static readonly byte[] Prefix = Encoding.ASCII.GetBytes("appID");

    /// <summary>Returns the application account of an application id.</summary>
    public static DomainAddress Of(ulong applicationId)
    {
        var input = new byte[Prefix.Length + sizeof(ulong)];
        Prefix.CopyTo(input, 0);
        BinaryPrimitives.WriteUInt64BigEndian(input.AsSpan(Prefix.Length), applicationId);
        return new DomainAddress(Sha512t256.Hash(input));
    }
}

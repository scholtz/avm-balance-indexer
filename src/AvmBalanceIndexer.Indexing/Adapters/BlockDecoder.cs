using Algorand.Algod.Model;
using Algorand.Algod.Model.Transactions;
using Algorand.Utils;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Decodes and validates a gossip block response.</summary>
public static class BlockDecoder
{
    private const int HashLength = 32;

    /// <summary>
    /// Decodes the payload with the Algorand5 codec, attaches the apply data, and checks that it is the
    /// requested round of the expected chain.
    /// </summary>
    /// <param name="payload">Raw msgpack body returned by an archival node.</param>
    /// <param name="expectedRound">Round that was requested.</param>
    /// <param name="profile">Chain the block must belong to.</param>
    /// <param name="expectedGenesisHash">Genesis hash the header must carry, or null to skip the check.</param>
    /// <remarks>
    /// The block hash is recomputed from the header and must equal the certificate proposal digest, so the hash used for
    /// chain linking is the real block hash. Certificate signatures and the transaction root are not verified; the archival
    /// source remains trusted for the transactions it serves.
    /// </remarks>
    /// <exception cref="BlockValidationException">The response is not the expected block.</exception>
    public static BlockData Decode(byte[] payload, ulong expectedRound, ChainProfile profile, byte[]? expectedGenesisHash)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(profile);

        CertifiedBlock certified;
        IReadOnlyList<TxnApplyData> apply;
        byte[] computedHash;
        try
        {
            certified = Encoder.DecodeFromMsgPack<CertifiedBlock>(payload);
            apply = ApplyDataReader.ReadBlockTransactions(payload);
            computedHash = BlockHashCalculator.Compute(payload);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever the SDK or the readers throw for garbage input, it is this host response that is bad: the
            // fetcher retries other hosts instead of the process failing.
            throw new BlockValidationException($"Round {expectedRound}: response is not a decodable block.", ex);
        }

        var block = certified?.Block ?? throw new BlockValidationException($"Round {expectedRound}: response has no block.");
        if (block.Round != expectedRound)
        {
            throw new BlockValidationException($"Requested round {expectedRound} but received round {block.Round}.");
        }

        if (!string.Equals(block.GenesisId, profile.GenesisId, StringComparison.Ordinal))
        {
            throw new BlockValidationException($"Round {expectedRound}: genesis id \"{block.GenesisId}\" is not \"{profile.GenesisId}\".");
        }

        var genesisHash = RequireHash(block.GenesisHash, "genesis hash", expectedRound);
        if (expectedGenesisHash is not null && !genesisHash.AsSpan().SequenceEqual(expectedGenesisHash))
        {
            throw new BlockValidationException($"Round {expectedRound}: genesis hash does not match the configured chain.");
        }

        var previous = RequireHash(block.PreviousBlockHash, "previous block hash", expectedRound);
        var cert = certified.Cert;
        if (cert?.Round != expectedRound)
        {
            throw new BlockValidationException($"Round {expectedRound}: certificate is missing or for round {cert?.Round}.");
        }

        var hash = RequireHash(cert.Prop?.Dig, "certified proposal digest", expectedRound);
        if (!computedHash.AsSpan().SequenceEqual(hash))
        {
            throw new BlockValidationException($"Round {expectedRound}: the block header does not hash to the certified digest.");
        }

        EnsureShapesMatch(block.Transactions ?? [], apply, expectedRound);
        return new BlockData(expectedRound, hash, previous, genesisHash, profile.GenesisId, block, apply);
    }

    private static byte[] RequireHash(byte[]? value, string name, ulong round) =>
        value is { Length: HashLength }
            ? value
            : throw new BlockValidationException($"Round {round}: {name} is missing or not {HashLength} bytes.");

    private static void EnsureShapesMatch(IEnumerable<SignedTransaction> transactions, IReadOnlyList<TxnApplyData> apply, ulong round)
    {
        var txs = transactions.ToArray();
        if (txs.Length != apply.Count)
        {
            throw new BlockValidationException($"Round {round}: {txs.Length} transactions decoded but {apply.Count} apply entries read.");
        }

        for (var i = 0; i < txs.Length; i++)
        {
            var inner = txs[i].Detail?.InnerTxns ?? [];
            EnsureShapesMatch(inner, apply[i].InnerTxns, round);
        }
    }
}

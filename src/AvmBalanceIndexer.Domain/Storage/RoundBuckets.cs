namespace AvmBalanceIndexer.Domain.Storage;

/// <summary>
/// Fixed-size round buckets that bound partition growth in both providers. The size is part of the on-disk layout:
/// changing it requires a replay into a fresh database.
/// </summary>
public static class RoundBuckets
{
    public const ulong Size = 100_000;

    public static int ForRound(ulong round) => checked((int)(round / Size));
}

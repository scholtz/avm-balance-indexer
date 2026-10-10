using System.ComponentModel.DataAnnotations;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>Tuning for the ordered round indexer.</summary>
public sealed class IndexerOptions
{
    public const string SectionName = "Indexer";

    /// <summary>Blocks fetched in parallel ahead of the commit position.</summary>
    [Range(1, 256)]
    public int FetchConcurrency { get; set; } = 8;

    /// <summary>How long to wait at the chain tip before asking for the latest round again.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan FollowPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound of cached account balances; the cache is cleared when exceeded.</summary>
    [Range(1_000, 50_000_000)]
    public int BalanceCacheEntries { get; set; } = 500_000;

    /// <summary>Progress is logged every this many rounds while backfilling.</summary>
    [Range(1, 10_000_000)]
    public int LogEveryRounds { get; set; } = 1_000;

    /// <summary>A round within this distance of the target counts as following the tip rather than backfilling.</summary>
    [Range(0, 10_000)]
    public int FollowingThresholdRounds { get; set; } = 50;
}

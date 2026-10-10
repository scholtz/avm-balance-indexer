using System.ComponentModel.DataAnnotations;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>Tuning for the ordered round indexer.</summary>
public sealed class IndexerOptions : IValidatableObject
{
    public const string SectionName = "Indexer";

    /// <summary>Blocks fetched in parallel ahead of the commit position.</summary>
    [Range(1, 256)]
    public int FetchConcurrency { get; set; } = 8;

    /// <summary>How long to wait at the chain tip before asking for the latest round again.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan FollowPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Upper bound of cached account balances. The cache keeps two generations of half this size each, and a round that
    /// touches more accounts than one generation holds bypasses it. Storage always supplies what is not cached.
    /// </summary>
    [Range(1_000, 50_000_000)]
    public int BalanceCacheEntries { get; set; } = 500_000;

    /// <summary>Progress is logged every this many rounds while backfilling.</summary>
    [Range(1, 10_000_000)]
    public int LogEveryRounds { get; set; } = 1_000;

    /// <summary>
    /// The worker stops (so the orchestrator can restart and alert) after this many consecutive failures without any
    /// round being indexed in between.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxConsecutiveFailures { get; set; } = 30;

    /// <summary>First wait after a transient failure; doubled per failure without progress up to <see cref="RetryMaxBackoff"/>.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:10:00")]
    public TimeSpan RetryInitialBackoff { get; set; } = TimeSpan.FromSeconds(2);

    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan RetryMaxBackoff { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>A round within this distance of the target counts as following the tip rather than backfilling.</summary>
    [Range(0, 10_000)]
    public int FollowingThresholdRounds { get; set; } = 50;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RetryMaxBackoff < RetryInitialBackoff)
        {
            yield return new ValidationResult(
                "Indexer:RetryMaxBackoff must not be smaller than Indexer:RetryInitialBackoff.",
                [nameof(RetryMaxBackoff)]);
        }
    }
}

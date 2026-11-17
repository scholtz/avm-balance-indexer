using System.ComponentModel.DataAnnotations;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>Tuning for the ordered round indexer.</summary>
public sealed class IndexerOptions : IValidatableObject
{
    public const string SectionName = "Indexer";

    /// <summary>
    /// Blocks fetched in parallel ahead of the commit position. Downloading is what limits a backfill (a block takes 50 to
    /// 700 ms from a gossip host, so 8 in flight is under 20 rounds per second); 128 gives about 60 to 150. Every block in
    /// flight is held decoded (typically 20 to 100 KB), so memory grows with this value.
    /// </summary>
    [Range(1, 1024)]
    public int FetchConcurrency { get; set; } = 128;

    /// <summary>
    /// While backfilling, this many consecutive rounds are committed in one database transaction (one cursor update, a handful
    /// of statements). Near the chain tip every round is committed on its own. 1 turns batching off.
    /// </summary>
    [Range(1, 1000)]
    public int CommitBatchRounds { get; set; } = 25;

    /// <summary>A batch is also committed once it holds this many effects, which bounds the memory and statement size of a commit.</summary>
    [Range(1, 1_000_000)]
    public int CommitBatchEffects { get; set; } = 20_000;

    /// <summary>How long to wait at the chain tip before asking for the latest round again.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan FollowPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reads name, symbol, decimals and total supply of ARC-200 tokens from their applications through algod's <c>simulate</c>
    /// (enrichment: best effort, never part of the ledger state). Turn it off for air-gapped deployments or benchmark runs.
    /// </summary>
    public bool Arc200Metadata { get; set; } = true;

    /// <summary>How long to wait before asking again for the metadata of a token whose lookup failed.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "1.00:00:00")]
    public TimeSpan Arc200MetadataRetryInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When algod's latest round has not advanced for this long (blocks come every ~3 s), the idle writer stops refreshing the
    /// published status, so the API reports it as stalled instead of healthy while the chain or the node is stuck. The API reports
    /// <c>Stalled</c> this value plus <c>Api:StalledAfterSeconds</c> after the last advance, so keep it below what an outage may last.
    /// A chain that produces blocks only on demand (a dev or sandbox network) needs a large value (up to 1 h) or 0, which turns the check off.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan TipStaleAfter { get; set; } = TimeSpan.FromSeconds(60);

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

    /// <summary>
    /// Longest wait after the node answered that its daily quota is used up (HTTP 403 naming the quota, as the public Nodely endpoints do).
    /// Such an answer is expected, does not count towards <see cref="MaxConsecutiveFailures"/> and ends by itself when the quota is renewed.
    /// The wait starts at <see cref="RetryInitialBackoff"/> (near the quota only a share of the requests is refused) and doubles up to
    /// this value while nothing is indexed in between (every refused request counts against the quota too).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "01:00:00")]
    public TimeSpan QuotaBackoff { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How often a standby replica asks again for the writer lease.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:10:00")]
    public TimeSpan LeaseRetryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>A round within this distance of the target counts as following the tip rather than backfilling.</summary>
    [Range(0, 10_000)]
    public int FollowingThresholdRounds { get; set; } = 50;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TipStaleAfter != TimeSpan.Zero && TipStaleAfter < TimeSpan.FromSeconds(10))
        {
            yield return new ValidationResult(
                "Indexer:TipStaleAfter must be 0 (off) or at least 10 seconds: blocks come every few seconds.",
                [nameof(TipStaleAfter)]);
        }

        if (RetryMaxBackoff < RetryInitialBackoff)
        {
            yield return new ValidationResult(
                "Indexer:RetryMaxBackoff must not be smaller than Indexer:RetryInitialBackoff.",
                [nameof(RetryMaxBackoff)]);
        }
    }
}

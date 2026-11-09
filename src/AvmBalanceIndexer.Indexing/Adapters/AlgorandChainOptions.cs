using System.ComponentModel.DataAnnotations;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Connection settings for one chain's archival block source and algod node.</summary>
public sealed class AlgorandChainOptions : IValidatableObject
{
    public const string SectionName = "Algorand";

    /// <summary>Chain id, one of the supported <see cref="Domain.Chains.ChainId"/> values.</summary>
    [Required]
    public string Chain { get; set; } = string.Empty;

    /// <summary>Algod base URL used for the latest round. Defaults to the chain profile's public endpoint.</summary>
    public string? AlgodUrl { get; set; }

    /// <summary>Algod API token, when the node requires one.</summary>
    public string? AlgodToken { get; set; }

    /// <summary>
    /// Archival gossip node base URLs, for example <c>http://archive.internal:4160</c>.
    /// When empty the hosts are discovered through the network's DNS SRV record.
    /// </summary>
    public string[] GossipHosts { get; set; } = [];

    /// <summary>Expected base64 genesis hash. Required for chains whose profile does not pin one.</summary>
    public string? ExpectedGenesisHash { get; set; }

    /// <summary>How long discovered gossip hosts are cached before DNS is queried again.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00")]
    public TimeSpan HostRefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Total attempts (across hosts) before a block fetch fails.</summary>
    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Timeout of one HTTP attempt.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>First retry delay; doubled per attempt up to <see cref="MaxRetryDelay"/> with full jitter.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:01:00")]
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a host that failed is skipped while healthy hosts exist.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan HostCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Most requests in flight to one gossip node. The limit adapts below this value: it halves whenever the node answers
    /// 429 and grows back as requests succeed, so it settles at what the node tolerates.
    /// </summary>
    [Range(1, 512)]
    public int MaxConcurrentRequestsPerHost { get; set; } = 32;

    /// <summary>
    /// Longest pause of a gossip node that rate-limits (HTTP 429, or 503 with <c>Retry-After</c>). The node's own
    /// <c>Retry-After</c> is honoured up to this value; without one the pause starts at one second and doubles.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan MaxThrottleDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Rate-limited nodes are logged as one summary per this interval (the first pause at once) instead of one line per pause.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan ThrottleLogInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long one block fetch keeps waiting while the nodes rate-limit it before it fails (a transient failure the worker
    /// retries). Throttled answers do not use up <see cref="MaxAttempts"/>.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan ThrottleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Rounds within this distance of the chain tip are also fetched from relays discovered through DNS (they hold only recent
    /// blocks), not just from archival nodes. Older rounds always go to archival nodes. 0 turns relays off. Has no effect when
    /// <see cref="GossipHosts"/> is set.
    /// </summary>
    [Range(0, 1_000)]
    public int RecentRoundsWindow { get; set; } = 100;

    /// <summary>Upper bound for one block response, protecting memory from misbehaving nodes.</summary>
    [Range(1024, 512 * 1024 * 1024)]
    public int MaxBlockBytes { get; set; } = 64 * 1024 * 1024;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MaxRetryDelay < BaseRetryDelay)
        {
            yield return new ValidationResult(
                "Algorand:MaxRetryDelay must not be smaller than Algorand:BaseRetryDelay.",
                [nameof(MaxRetryDelay)]);
        }

        if (ThrottleTimeout < MaxThrottleDelay)
        {
            yield return new ValidationResult(
                "Algorand:ThrottleTimeout must not be smaller than Algorand:MaxThrottleDelay, otherwise a single pause of a node could never fit into it.",
                [nameof(ThrottleTimeout)]);
        }
    }
}

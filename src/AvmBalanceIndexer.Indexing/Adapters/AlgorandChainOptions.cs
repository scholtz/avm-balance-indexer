using System.ComponentModel.DataAnnotations;

namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>Connection settings for one chain's archival block source and algod node.</summary>
public sealed class AlgorandChainOptions
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
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>First retry delay; doubled per attempt up to <see cref="MaxRetryDelay"/> with full jitter.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:01:00")]
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a host that failed is skipped while healthy hosts exist.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan HostCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound for one block response, protecting memory from misbehaving nodes.</summary>
    [Range(1024, 512 * 1024 * 1024)]
    public int MaxBlockBytes { get; set; } = 64 * 1024 * 1024;
}

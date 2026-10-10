namespace AvmBalanceIndexer.Indexing.Adapters;

/// <summary>A fixed host list, typically the operator-owned archival nodes.</summary>
public sealed class StaticGossipHostProvider : IGossipHostProvider
{
    private readonly string[] _hosts;

    public StaticGossipHostProvider(IEnumerable<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        _hosts = hosts.Select(static h => h.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_hosts.Length == 0)
        {
            throw new ArgumentException("At least one gossip host is required.", nameof(hosts));
        }
    }

    public Task<IReadOnlyList<string>> GetHostsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_hosts);
}

using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AvmBalanceIndexer.Api;

/// <summary>
/// Ready means the configured database answers. A database the worker has not initialised yet is still reachable, so the
/// API is ready and reports that nothing has been indexed.
/// </summary>
public sealed class StorageHealthCheck(IChainStorage storage, ChainContext chain) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.GetAsync(chain.Chain, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The storage provider is not reachable.", ex);
        }
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace AvmBalanceIndexer.Storage.Cassandra;

public static class CassandraStorageExtensions
{
    /// <summary>
    /// The Cassandra schema is defined (Schema/0001_initial.cql) but the storage implementation is not written yet.
    /// Failing at startup is deliberate: a deployment configured for Cassandra must not silently run without storage.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public static IServiceCollection AddCassandraStorage(this IServiceCollection services) =>
        throw new NotSupportedException(
            "The Cassandra storage provider is not implemented yet. Set Storage:Provider to PostgreSql.");
}

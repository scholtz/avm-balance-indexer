using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AvmBalanceIndexer.Storage.Cassandra;

public static class CassandraStorageExtensions
{
    /// <summary>Registers the Apache Cassandra implementation of the storage contract.</summary>
    public static IServiceCollection AddCassandraStorage(this IServiceCollection services)
    {
        services.AddSingleton<ISchemaManager, CassandraSchemaManager>();
        return services;
    }
}

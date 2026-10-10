using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public static class PostgreSqlStorageExtensions
{
    /// <summary>Registers the PostgreSQL implementation of the storage contract.</summary>
    public static IServiceCollection AddPostgreSqlStorage(this IServiceCollection services)
    {
        services.AddSingleton<ISchemaManager, PostgreSqlSchemaManager>();
        return services;
    }
}

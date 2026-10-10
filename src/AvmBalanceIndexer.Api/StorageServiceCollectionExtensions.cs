using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Storage.Cassandra;
using AvmBalanceIndexer.Storage.PostgreSql;

namespace AvmBalanceIndexer.Api;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="StorageOptions"/> and registers only the provider selected by Storage:Provider.
    /// Switching providers is a configuration change followed by a replay; never dual-write.
    /// </summary>
    public static IServiceCollection AddChainStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(StorageOptions.SectionName);
        services.AddOptions<StorageOptions>().Bind(section).ValidateOnStart();

        var provider = section.GetValue<StorageProvider>("Provider");
        return provider switch
        {
            StorageProvider.PostgreSql => services.AddPostgreSqlStorage(),
            StorageProvider.Cassandra => services.AddCassandraStorage(),
            _ => throw new InvalidOperationException($"Unsupported Storage:Provider '{provider}'."),
        };
    }
}

using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AvmBalanceIndexer.Storage.PostgreSql;

public static class PostgreSqlStorageExtensions
{
    /// <summary>
    /// Registers the PostgreSQL implementation of the storage contract. Requires <see cref="StorageOptions"/> and a
    /// <see cref="ChainContext"/> to be registered by the host.
    /// </summary>
    public static IServiceCollection AddPostgreSqlStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StorageOptions>, PostgreSqlOptionsValidator>());
        services.AddSingleton(static provider =>
        {
            var settings = provider.GetRequiredService<IOptions<StorageOptions>>().Value.PostgreSql;

            // Npgsql's own command timeout must not fire before the server's statement timeout (which is the one that is translated).
            var connection = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
            connection.CommandTimeout = Math.Max(connection.CommandTimeout, settings.ReadTimeoutSeconds + 5);
            return NpgsqlDataSource.Create(connection.ConnectionString);
        });
        services.AddSingleton(static provider => new PostgreSqlChainStorage(
            provider.GetRequiredService<NpgsqlDataSource>(),
            provider.GetRequiredService<ChainContext>(),
            provider.GetRequiredService<IOptions<StorageOptions>>().Value.PostgreSql.AccountHashPartitions,
            TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<StorageOptions>>().Value.PostgreSql.ReadTimeoutSeconds)));
        services.AddSingleton<IChainStorage>(static provider => provider.GetRequiredService<PostgreSqlChainStorage>());
        return services;
    }

    private sealed class PostgreSqlOptionsValidator : IValidateOptions<StorageOptions>
    {
        public ValidateOptionsResult Validate(string? name, StorageOptions options)
        {
            if (options.Provider != StorageProvider.PostgreSql)
            {
                return ValidateOptionsResult.Success;
            }

            var settings = options.PostgreSql;
            if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                return ValidateOptionsResult.Fail("Storage:PostgreSql:ConnectionString is required when Storage:Provider is PostgreSql.");
            }

            if (settings.ReadTimeoutSeconds is < 1 or > 600)
            {
                return ValidateOptionsResult.Fail("Storage:PostgreSql:ReadTimeoutSeconds must be between 1 and 600.");
            }

            return settings.AccountHashPartitions is < 1 or > 1024
                ? ValidateOptionsResult.Fail("Storage:PostgreSql:AccountHashPartitions must be between 1 and 1024.")
                : ValidateOptionsResult.Success;
        }
    }
}

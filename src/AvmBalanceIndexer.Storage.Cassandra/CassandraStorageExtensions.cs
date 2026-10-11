using System.Text.RegularExpressions;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Storage.Cassandra;

public static class CassandraStorageExtensions
{
    /// <summary>
    /// Registers the Cassandra implementation of the storage contract. Requires <see cref="StorageOptions"/> and a
    /// <see cref="ChainContext"/> to be registered by the host. The connection is opened on first use.
    /// </summary>
    public static IServiceCollection AddCassandraStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StorageOptions>, CassandraOptionsValidator>());
        services.AddSingleton(static provider => new CassandraSessionSource(provider.GetRequiredService<IOptions<StorageOptions>>().Value.Cassandra));
        services.AddSingleton(static provider => new CassandraChainStorage(
            provider.GetRequiredService<CassandraSessionSource>(),
            provider.GetRequiredService<ChainContext>(),
            provider.GetRequiredService<IOptions<StorageOptions>>().Value.Cassandra));
        services.AddSingleton<IChainStorage>(static provider => provider.GetRequiredService<CassandraChainStorage>());
        return services;
    }
}

internal sealed partial class CassandraOptionsValidator : IValidateOptions<StorageOptions>
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,47}$")]
    private static partial Regex KeyspacePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex DatacenterPattern();

    internal static bool IsValidIdentifier(string? value) => value is not null && KeyspacePattern().IsMatch(value);

    internal static bool IsValidDatacenter(string? value) => value is not null && DatacenterPattern().IsMatch(value);

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (options.Provider != StorageProvider.Cassandra)
        {
            return ValidateOptionsResult.Success;
        }

        var settings = options.Cassandra;
        var errors = new List<string>();
        if (settings.ContactPoints.Length == 0 || settings.ContactPoints.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Storage:Cassandra:ContactPoints needs at least one host when Storage:Provider is Cassandra.");
        }

        if (settings.Port is < 1 or > 65535)
        {
            errors.Add("Storage:Cassandra:Port must be between 1 and 65535.");
        }

        if (!IsValidIdentifier(settings.Keyspace))
        {
            errors.Add("Storage:Cassandra:Keyspace must start with a letter and contain only letters, digits and underscores (at most 48 characters).");
        }

        if (!IsValidDatacenter(settings.LocalDatacenter))
        {
            errors.Add("Storage:Cassandra:LocalDatacenter must be a valid datacenter name.");
        }

        if (settings.ReplicationFactor is < 1 or > 9)
        {
            errors.Add("Storage:Cassandra:ReplicationFactor must be between 1 and 9.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Ingestion;
using AvmBalanceIndexer.Indexing.Normalization;
using AvmBalanceIndexer.Storage.Cassandra;
using AvmBalanceIndexer.Storage.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvmBalanceIndexer.Hosting;

/// <summary>Dependency injection wiring shared by the API and the indexer worker.</summary>
public static class HostingExtensions
{
    /// <summary>The configuration key that names the chain a deployment serves.</summary>
    public const string ChainKey = "Chain";

    /// <summary>
    /// Registers the chain context and the storage provider selected by <c>Storage:Provider</c>. Only the selected
    /// provider is registered, so only its database is ever opened.
    /// </summary>
    /// <exception cref="InvalidOperationException">The chain or the provider is missing or unsupported.</exception>
    public static IServiceCollection AddChainStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var chain = configuration[ChainKey];
        if (string.IsNullOrWhiteSpace(chain) || !ChainId.IsKnown(chain))
        {
            throw new InvalidOperationException($"Configuration key {ChainKey} must be one of: {string.Join(", ", ChainId.All)}.");
        }

        services.AddSingleton(new ChainContext(chain));

        var section = configuration.GetSection(StorageOptions.SectionName);
        services.AddOptions<StorageOptions>().Bind(section).ValidateOnStart();

        var provider = section.GetValue<StorageProvider?>("Provider")
            ?? throw new InvalidOperationException("Configuration key Storage:Provider is required (PostgreSql or Cassandra).");
        return provider switch
        {
            StorageProvider.PostgreSql => services.AddPostgreSqlStorage(),
            StorageProvider.Cassandra => services.AddCassandraStorage(),
            _ => throw new InvalidOperationException($"Unsupported Storage:Provider {provider}."),
        };
    }

    /// <summary>
    /// Registers the archival block source, the native token effect normalizer and the round indexer. Requires
    /// <see cref="AddChainStorage"/> to have been called.
    /// </summary>
    public static IServiceCollection AddAlgorandIndexing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AlgorandChainOptions>()
            .Bind(configuration.GetSection(AlgorandChainOptions.SectionName))
            .Configure(options => options.Chain = string.IsNullOrWhiteSpace(options.Chain) ? configuration[ChainKey] ?? string.Empty : options.Chain)
            .Validate(
                options => string.IsNullOrWhiteSpace(configuration[ChainKey]) || string.Equals(options.Chain, configuration[ChainKey], StringComparison.Ordinal),
                "Algorand:Chain must equal the top-level Chain setting; a deployment serves one chain.")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AlgorandChainOptions>, AlgorandChainOptionsValidator>());

        services.AddOptions<IndexerOptions>()
            .Bind(configuration.GetSection(IndexerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IChainAdapter>(static provider => AlgorandChainFactory.Create(
            provider.GetRequiredService<IOptions<AlgorandChainOptions>>().Value,
            provider.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IEffectNormalizer, AlgoEffectNormalizer>();
        services.AddSingleton<RoundIndexer>();
        return services;
    }

    private sealed class AlgorandChainOptionsValidator : IValidateOptions<AlgorandChainOptions>
    {
        public ValidateOptionsResult Validate(string? name, AlgorandChainOptions options)
        {
            if (!ChainId.IsKnown(options.Chain))
            {
                return ValidateOptionsResult.Fail($"Algorand:Chain must be one of: {string.Join(", ", ChainId.All)}.");
            }

            try
            {
                _ = AlgorandChainFactory.ResolveGenesisHash(ChainProfile.For(options.Chain), options);
            }
            catch (InvalidOperationException ex)
            {
                return ValidateOptionsResult.Fail(ex.Message);
            }

            return options.GossipHosts.All(static h => Uri.TryCreate(h, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Algorand:GossipHosts must be absolute http(s) URLs.");
        }
    }
}

using AvmBalanceIndexer.Domain.Chains;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace AvmBalanceIndexer.Hosting;

public static class LoggingExtensions
{
    /// <summary>The setting that selects the console format: <c>Text</c> (default) or <c>Json</c> (one compact JSON object per line).</summary>
    public const string FormatSetting = "Logging:Format";

    private const string ConsoleTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Sends all logging through Serilog. Levels, overrides and sinks come from the <c>Serilog</c> configuration section (so also from
    /// <c>Serilog__MinimumLevel__Default=Debug</c> in the environment); the Microsoft <c>Logging:LogLevel</c> filters are not used,
    /// Serilog is the only place levels are decided. Without a configured <c>Serilog:WriteTo</c> the log goes to the console, as text or
    /// as compact JSON (<see cref="FormatSetting"/>). Every event carries the <c>Service</c> and, when configured, the <c>Chain</c>.
    /// </summary>
    public static IServiceCollection AddAvmLogging(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var json = string.Equals(configuration[FormatSetting], "Json", StringComparison.OrdinalIgnoreCase);
        var customSinks = configuration.GetSection("Serilog:WriteTo").Exists();
        var chain = configuration["Chain"];

        // Serilog decides what is logged; the framework's own filters (Logging:LogLevel) would only hide events from it.
        services.Configure<LoggerFilterOptions>(options =>
        {
            options.Rules.Clear();
            options.MinLevel = LogLevel.Trace;
        });

        services.AddSerilog((provider, logger) =>
        {
            logger
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .ReadFrom.Configuration(configuration)
                .ReadFrom.Services(provider)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName);
            if (!string.IsNullOrWhiteSpace(chain) && ChainId.IsKnown(chain))
            {
                logger.Enrich.WithProperty("Chain", chain);
            }

            if (!customSinks)
            {
                if (json)
                {
                    logger.WriteTo.Console(new RenderedCompactJsonFormatter());
                }
                else
                {
                    logger.WriteTo.Console(outputTemplate: ConsoleTemplate);
                }
            }
        });
        return services;
    }
}

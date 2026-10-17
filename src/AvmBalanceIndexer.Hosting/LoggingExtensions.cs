using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace AvmBalanceIndexer.Hosting;

public static partial class LoggingExtensions
{
    /// <summary>Warns that <c>Logging:LogLevel</c> settings (an earlier release's way to set levels) have no effect any more.</summary>
    internal sealed partial class IgnoredLogLevelNotice(ILogger<IgnoredLogLevelNotice> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            LogIgnored(logger);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Logging:LogLevel settings are ignored: Serilog decides the levels. Use Serilog:MinimumLevel (Serilog__MinimumLevel__Default) instead.")]
        private static partial void LogIgnored(ILogger<IgnoredLogLevelNotice> logger);
    }

    /// <summary>The setting that selects the console format: <c>Text</c> (default) or <c>Json</c> (one compact JSON object per line).</summary>
    public const string FormatSetting = "Logging:Format";

    private const string ConsoleTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Sends all logging through Serilog. Levels, overrides and sinks come from the <c>Serilog</c> configuration section (so also from
    /// <c>Serilog__MinimumLevel__Default=Debug</c> in the environment); the Microsoft <c>Logging:LogLevel</c> filters are not used
    /// (Serilog replaces the logger factory, so they never apply; a startup warning names such a setting). Without a configured <c>Serilog:WriteTo</c> the log goes to the console, as text or
    /// as compact JSON (<see cref="FormatSetting"/>). Every event carries the <c>Service</c> and, when configured, the <c>Chain</c>.
    /// </summary>
    public static IServiceCollection AddAvmLogging(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var format = configuration[FormatSetting];
        if (!string.IsNullOrWhiteSpace(format) && !string.Equals(format, "Json", StringComparison.OrdinalIgnoreCase) && !string.Equals(format, "Text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{FormatSetting} must be Text or Json, not '{format}'.");
        }


        if (configuration.GetSection("Logging:LogLevel").Exists())
        {
            services.AddHostedService<IgnoredLogLevelNotice>();
        }

        services.AddSerilog((provider, logger) =>
        {
            // Read when the logger is built, from the same configuration that ReadFrom.Configuration sees.
            var json = string.Equals(configuration[FormatSetting], "Json", StringComparison.OrdinalIgnoreCase);
            var customSinks = configuration.GetSection("Serilog:WriteTo").Exists();
            var chain = configuration["Chain"];
            logger
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .ReadFrom.Configuration(configuration)
                .ReadFrom.Services(provider)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName);
            if (!string.IsNullOrWhiteSpace(chain))
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

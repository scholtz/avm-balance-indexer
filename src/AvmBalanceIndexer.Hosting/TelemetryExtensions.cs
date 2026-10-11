using AvmBalanceIndexer.Domain.Chains;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace AvmBalanceIndexer.Hosting;

public static class TelemetryExtensions
{
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Meters of the ASP.NET Core runtime that describe request volume, latency and connections.</summary>
    public static readonly string[] AspNetCoreMeters =
    [
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Diagnostics",
    ];

    /// <summary>
    /// Publishes the given meters and the .NET runtime metrics over OTLP (any OpenTelemetry collector, or Prometheus with its OTLP
    /// receiver). Off unless an OTLP endpoint is configured (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, which also selects protocol, headers and
    /// interval through the standard <c>OTEL_*</c> variables) or <c>Telemetry:Enabled</c> is true; nothing is started otherwise.
    /// </summary>
    public static IServiceCollection AddAvmTelemetry(this IServiceCollection services, IConfiguration configuration, string serviceName, params string[] meters)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var enabled = configuration.GetValue<bool?>("Telemetry:Enabled")
            ?? !string.IsNullOrWhiteSpace(configuration[EndpointVariable]);
        if (!enabled)
        {
            return services;
        }

        var chain = configuration["Chain"];
        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(serviceName);
                if (!string.IsNullOrWhiteSpace(chain) && ChainId.IsKnown(chain))
                {
                    resource.AddAttributes([new KeyValuePair<string, object>("avm.chain", chain)]);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(meters).AddRuntimeInstrumentation().AddOtlpExporter();
            });
        return services;
    }
}

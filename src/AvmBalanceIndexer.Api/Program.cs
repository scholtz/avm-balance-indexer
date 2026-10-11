using AvmBalanceIndexer.Api;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);
builder.Services.AddChainStorage(builder.Configuration);
builder.Services.AddAvmTelemetry(builder.Configuration, "avm-api", TelemetryExtensions.AspNetCoreMeters);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness only proves the process responds; readiness also requires the database.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = static _ => false });
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = static check => check.Tags.Contains("ready") });

// One process serves exactly one chain; requests for any other chain are rejected explicitly.
var v1 = app.MapGroup("/v1/{chain}")
    .AddEndpointFilter(async (context, next) =>
    {
        var requested = context.HttpContext.GetRouteValue("chain") as string;
        var served = context.HttpContext.RequestServices.GetRequiredService<ChainContext>().Chain;
        return string.Equals(requested, served, StringComparison.Ordinal)
            ? await next(context)
            : Results.NotFound(new { error = "This deployment does not serve that chain.", requested, served });
    });

v1.MapGet(
    "/status",
    async (ChainContext chain, IChainStorage storage, IOptions<StorageOptions> options, CancellationToken cancellationToken) =>
    {
        var status = await storage.GetAsync(chain.Chain, cancellationToken);
        return status is null
            ? Results.Ok(new { chain = chain.Chain, provider = options.Value.Provider.ToString(), state = "NotStarted", indexedRound = (ulong?)null })
            : Results.Ok(new
            {
                chain = chain.Chain,
                provider = options.Value.Provider.ToString(),
                state = status.State.ToString(),
                indexedRound = (ulong?)status.IndexedRound,
                targetRound = status.TargetRound,
                lag = status.Lag,
                updatedAt = status.UpdatedAt,
            });
    })
    .WithName("GetChainStatus");

v1.MapReadEndpoints();

app.Run();

public partial class Program;

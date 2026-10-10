using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Hosting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddChainStorage(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

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

app.Run();

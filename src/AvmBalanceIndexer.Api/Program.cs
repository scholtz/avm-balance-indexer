using AvmBalanceIndexer.Api;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;

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

var v1 = app.MapGroup("/v1/{chain}")
    .AddEndpointFilter(async (context, next) =>
    {
        var chain = context.HttpContext.GetRouteValue("chain") as string;
        return chain is not null && ChainId.IsKnown(chain)
            ? await next(context)
            : Results.NotFound(new { error = "unknown chain", chain });
    });

v1.MapGet("/status", (string chain, IConfiguration configuration) => Results.Ok(new
{
    chain,
    provider = configuration[$"{StorageOptions.SectionName}:Provider"],
    indexedRound = 0UL,
    targetRound = (ulong?)null,
    lag = 0UL,
    state = IngestionState.Bootstrapping.ToString(),
}))
.WithName("GetChainStatus");

app.Run();

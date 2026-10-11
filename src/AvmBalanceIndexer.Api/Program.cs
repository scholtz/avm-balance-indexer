using AvmBalanceIndexer.Api;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Hosting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ApiOptions>().Bind(builder.Configuration.GetSection(ApiOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
var api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
builder.Services.AddOpenApi();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
});
builder.Services.AddRequestTimeouts(options =>
    options.AddPolicy("data", TimeSpan.FromSeconds(Math.Clamp(api.RequestTimeoutSeconds, 1, 600))));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse("Too many requests; slow down."), cancellationToken);
    };
    options.AddPolicy("client", httpContext =>
        api.RateLimitPerMinute == 0
            ? RateLimitPartition.GetNoLimiter("unlimited")
            : RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = api.RateLimitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
if (api.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Behind an ingress the proxy address is not known in advance; the option is only meant for such deployments.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
builder.Services.AddHealthChecks().AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);
builder.Services.AddChainStorage(builder.Configuration);
builder.Services.AddAvmTelemetry(builder.Configuration, "avm-api", TelemetryExtensions.AspNetCoreMeters.ToArray());
builder.Services.AddOptions<ScanOptions>().Bind(builder.Configuration.GetSection(ScanOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (api.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});
app.UseResponseCompression();
app.UseRateLimiter();
app.UseRequestTimeouts();

if (api.PublishOpenApi)
{
    app.MapOpenApi().DisableRateLimiting();
}

// Liveness only proves the process responds; readiness also requires the database.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = static _ => false });
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = static check => check.Tags.Contains("ready") });

// One process serves exactly one chain; requests for any other chain are rejected explicitly.
var v1 = app.MapGroup("/v1/{chain}");
v1.RequireRateLimiting("client");
v1.WithRequestTimeout("data");
v1.AddEndpointFilter(async (context, next) =>
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
    .WithName("GetChainStatus")
    .WithTags("Status")
    .WithSummary("Indexed round, target round, lag and ingestion state")
    .WithErrors(StatusCodes.Status404NotFound)
    .WithCommonErrors();

v1.MapReadEndpoints();
v1.MapScanEndpoints();

app.Run();

public partial class Program;

using AvmBalanceIndexer.Api;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Hosting;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
Validator.ValidateObject(api, new ValidationContext(api), validateAllProperties: true);
builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, _) =>
{
    // The handlers read the chain from the deployment, so the description would not mention the {chain} segment of the path at all and
    // "Try it out" could not fill it. Declare it, fixed to the one chain this deployment serves.
    if (context.Description.RelativePath?.Contains("{chain}", StringComparison.Ordinal) == true)
    {
        var served = context.ApplicationServices.GetRequiredService<ChainContext>().Chain;
        operation.Parameters ??= [];
        if (!operation.Parameters.Any(static p => p.Name == "chain"))
        {
            operation.Parameters.Add(new Microsoft.OpenApi.OpenApiParameter
            {
                Name = "chain",
                In = Microsoft.OpenApi.ParameterLocation.Path,
                Required = true,
                Description = "The chain this deployment serves.",
                Schema = new Microsoft.OpenApi.OpenApiSchema
                {
                    Type = Microsoft.OpenApi.JsonSchemaType.String,
                    Default = System.Text.Json.Nodes.JsonValue.Create(served),
                    Enum = [System.Text.Json.Nodes.JsonValue.Create(served)],
                },
            });
        }
    }

    return Task.CompletedTask;
}));
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
});
builder.Services.AddRequestTimeouts(options =>
    options.AddPolicy("data", TimeSpan.FromSeconds(api.RequestTimeoutSeconds)));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? (int)Math.Ceiling(retryAfter.TotalSeconds) : 60;
        context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, wait).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse("Too many requests; slow down."), cancellationToken);
    };

    // Requests that reach no endpoint (the Swagger UI files are served by middleware) are limited by this one; everything else is
    // limited by the "client" policy on its endpoints, so health checks stay free.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        api.RateLimitPerMinute == 0 || !httpContext.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)
            ? RateLimitPartition.GetNoLimiter("unlimited")
            : RateLimitPartition.GetFixedWindowLimiter(
                "ui:" + ClientKey(httpContext.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = api.RateLimitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("client", httpContext =>
        api.RateLimitPerMinute == 0
            ? RateLimitPartition.GetNoLimiter("unlimited")
            : RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(httpContext.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = api.RateLimitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
if (api.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = api.ForwardedHeadersLimit;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var network in api.KnownProxyNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        // Without a list the sender is believed whoever it is: only for an API that is reachable through the proxy alone.
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

if (!string.IsNullOrEmpty(api.PathBase))
{
    app.UsePathBase(api.PathBase);
}

app.Use(async (context, next) =>
{
    // OnStarting, so the header is on error responses too (the exception handler clears headers it did not write).
    context.Response.OnStarting(static state =>
    {
        ((HttpResponse)state).Headers.XContentTypeOptions = "nosniff";
        return Task.CompletedTask;
    }, context.Response);
    await next();
});
app.UseExceptionHandler();
app.UseResponseCompression();
app.UseRateLimiter();
app.UseRequestTimeouts();

if (api.PublishOpenApi)
{
    app.MapOpenApi().RequireRateLimiting("client");

    // The UI ships its own scripts and styles (no CDN), and refers to the description relatively, so it also works behind an
    // ingress path prefix.
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("../openapi/v1.json", "AVM balance indexer v1");
        options.DocumentTitle = "AVM balance indexer API";
        options.DisplayRequestDuration();
    });

    // PathBase, so it also works behind an ingress that strips a path prefix.
    app.MapGet("/", (HttpRequest request) => Results.Redirect($"{request.PathBase}/swagger/index.html"))
        .RequireRateLimiting("client")
        .ExcludeFromDescription();
}
else
{
    app.MapGet(
        "/",
        (HttpRequest request, ChainContext chain) => Results.Ok(new
        {
            service = "avm-balance-indexer",
            chain = chain.Chain,
            status = $"{request.PathBase}/v1/{chain.Chain}/status",
            health = $"{request.PathBase}/healthz/ready",
        }))
        .RequireRateLimiting("client")
        .ExcludeFromDescription();
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
            ? Results.Ok(new StatusResponse(chain.Chain, options.Value.Provider.ToString(), "NotStarted", null, null, null, null))
            : Results.Ok(new StatusResponse(
                chain.Chain,
                options.Value.Provider.ToString(),
                status.State.ToString(),
                status.IndexedRound,
                status.TargetRound,
                status.Lag,
                status.UpdatedAt));
    })
    .WithName("GetChainStatus")
    .Produces<StatusResponse>()
    .WithTags("Status")
    .WithSummary("Indexed round, target round, lag and ingestion state")
    .WithErrors(StatusCodes.Status404NotFound)
    .WithCommonErrors();

v1.MapReadEndpoints();
v1.MapScanEndpoints();

app.Run();

public partial class Program
{
    /// <summary>
    /// The rate-limit identity of a client: IPv4-mapped IPv6 addresses count as the IPv4 address, and an IPv6 client is its /64 (the
    /// smallest block a subscriber normally owns), so rotating addresses inside it does not buy a fresh allowance.
    /// </summary>
    internal static string ClientKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return new IPAddress(bytes).ToString() + "/64";
    }
}

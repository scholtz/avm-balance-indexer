using System.ComponentModel.DataAnnotations;

namespace AvmBalanceIndexer.Api;

/// <summary>Bound from the Api configuration section: protections that make the read API safe to expose.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Requests per client IP address and minute on the data endpoints; 0 turns the limiter off.</summary>
    [Range(0, 10_000_000)]
    public int RateLimitPerMinute { get; set; } = 600;

    /// <summary>Longest a data request may take before it is answered with 504.</summary>
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Honour <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> (the client address then comes from the ingress). Only enable it when
    /// the API is reachable exclusively through a proxy you control, otherwise clients can pick their own address.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Serve the OpenAPI description at <c>/openapi/v1.json</c>.</summary>
    public bool PublishOpenApi { get; set; } = true;
}

/// <summary>The body of every error response.</summary>
public sealed record ErrorResponse(string Error);

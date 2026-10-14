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

    /// <summary>
    /// Networks (CIDR, for example <c>10.0.0.0/8</c>) whose proxies are believed. Empty trusts every sender, which is only right when the API
    /// cannot be reached except through the proxy.
    /// </summary>
    public string[] KnownProxyNetworks { get; set; } = [];

    /// <summary>How many proxy hops to peel off <c>X-Forwarded-For</c> (a CDN in front of an ingress is two).</summary>
    [Range(1, 10)]
    public int ForwardedHeadersLimit { get; set; } = 1;

    /// <summary>Serve the OpenAPI description at <c>/openapi/v1.json</c>.</summary>
    public bool PublishOpenApi { get; set; } = true;

    /// <summary>
    /// Path prefix the API is published under when the ingress forwards it without stripping it, for example <c>/algorand-testnet</c>. Empty
    /// serves from the root. The Swagger UI and the server address in the OpenAPI description include it.
    /// </summary>
    [RegularExpression(@"^(/[A-Za-z0-9._~-]+)*$", ErrorMessage = "Api:PathBase must be empty or start with a slash, have no trailing slash and contain only URL-safe segments (for example /algorand-testnet).")]
    public string PathBase { get; set; } = string.Empty;
}

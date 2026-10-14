# ADR 0015: A bundled Swagger UI in the API

- Status: accepted
- Date: 2026-10-14
- Issue: #96

## Context

The API published an OpenAPI description at `/openapi/v1.json` but no way to look at it, and the root answered 404, which is what a person sees
first when opening a deployment's address.

## Decision

- Serve Swagger UI at `/swagger`, and redirect `/` there, in every environment while `Api:PublishOpenApi` is true (the default).
  With it off, `/` answers a small JSON landing document instead of 404.
- Use `Swashbuckle.AspNetCore.SwaggerUI` only for its static assets. They are embedded in the package and served by the API itself: no CDN,
  so the page works in an air-gapped cluster and under a strict content policy. The description itself still comes from
  `Microsoft.AspNetCore.OpenApi`. A CDN-hosted UI or Scalar were rejected for the external dependency at runtime and the extra client
  library respectively.
- The description declares the `{chain}` path parameter, fixed to the chain the deployment serves (the handlers read the chain from the
  deployment, so the framework did not generate it), which is what lets "Try it out" fill the address.
- The UI files are rate limited per client like the data endpoints (a global limiter for `/swagger/*`, case-insensitive; the window is separate
  from the data endpoints'). "Try it out" is not on by default: the page also lists the scan endpoints that create work.
- `Api:PathBase` supports an ingress that forwards a path prefix; the redirect, the UI and the server address in the description include it.

## Consequences

- About 1.5 MB of third-party JavaScript is served by the API image (on request only).
- A client can use up to `Api:RateLimitPerMinute` requests per minute for the UI files and the same for the data endpoints.

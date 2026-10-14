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
- The description declares the `{chain}` path parameter, because the handlers read the chain from the deployment and the framework therefore does not generate it. Its default is the chain the deployment serves. It is a default and not an enum, so a client generated from any deployment can address another. The description names a relative server (`/`, or the path base), so the page calls the origin it was loaded from even behind a TLS-terminating ingress.
- The UI files are rate limited per client like the data endpoints (a global limiter for `/swagger/*`, case-insensitive; the window is separate
  from the data endpoints'). The UI only submits GET requests (the scan endpoints that create work are not executable from the public page) and "Try it out" is not on by default.
- Responses under `/swagger` carry a content security policy that allows only the API's own origin, which also stops a crafted `?url=` link from loading a foreign description.
- `Api:PathBase` supports an ingress that forwards a path prefix; the redirect, the UI and the server address in the description include it. Requests that arrive without the prefix (a port-forward to the pod) are still served.

## Consequences

- About 1.5 MB of third-party JavaScript is served by the API image (on request only; the package serves its files precompressed and the API's response compression skips `/swagger`). It updates with the package: Dependabot's weekly NuGet run proposes new versions, and an advisory in swagger-ui is fixed by merging that update; set `Api:PublishOpenApi` to false to remove the UI from a deployment.
- A client can use up to `Api:RateLimitPerMinute` requests per minute for the UI files and the same for the data endpoints.

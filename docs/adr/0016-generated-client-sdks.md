# ADR 0016: Client SDKs generated from the OpenAPI document

- Status: accepted
- Date: 2026-10-14
- Issue: #95

## Context

Integrators called the HTTP API by hand. We want typed clients for TypeScript, Python, .NET, Go and Rust that stay equal to the API, default to the
public deployments, work against any self-hosted deployment, and are published without manual work.

## Decision

- **The contract is a committed OpenAPI 3.0 document** (`sdk/openapi/avm-balance-indexer.v1.json`), rendered by the API itself. A test in the API suite fails when it differs from
  `/openapi/v1.json`. 3.0 (not the 3.1 default of .NET 10) because the generators read it reliably (3.1 unions such as `anyOf` integer/string and `type: [..]` arrays break several of them).
  The API shapes its document for generators (`OpenApiRefinements`): typed query parameters, shared `ChainId` and `TokenKind` enums, plain integers, an optional JSON body. The `chain` path parameter of ADR 0015 keeps its relative server; the chain the deployment serves became its `example` (the Swagger UI still prefills it) and its type the enum of all chains, which still lets a client generated from any deployment address another. A `default` next to the shared enum reference made the generators reorder the parameters and drop the enum type.
  Every error status, including the request timeout (504), has the JSON error body.
- **One generator per language, the idiomatic one**, with the generated code committed and marked `linguist-generated`: openapi-generator (TypeScript fetch, Python pydantic), NSwag (System.Text.Json), oapi-codegen (Go), progenitor (Rust).
  `sdk/scripts/generate.sh` pins every generator version (including the nightly rustfmt progenitor needs) so that the output is reproducible; CI regenerates and fails on any difference.
  Committed generated code (instead of generating at install or build time) keeps the packages installable without the toolchains, reviewable, and independent of generator availability.
- **A small hand-written layer per language holds the product behaviour**, identical across languages: public deployments as defaults, custom base URL/headers/timeout/HTTP client, one client per chain, pagination that pins the round, the holder export on top of the scan API
  (keeps its cursor while a scan runs, waits instead of polling hot, cancels the scan when the consumer stops early), typed errors, no hidden retries. Tests run against a local mock server.
- **One lock-step version** (`sdk/VERSION`) for all SDKs; `sdk/scripts/check-versions.sh` enforces that the manifests agree.
- **Publishing is automatic and idempotent**: a merge to `main` that changes `sdk/` runs the SDK checks, then publishes each registry whose secret is configured and whose version does not exist yet (npm, PyPI, NuGet, crates.io), then tags the Go module.
  Tokens are visible only to the publishing step; the jobs run in the environment `sdk-publish` (optional reviewers); only `main` publishes. See `docs/sdk-publishing.md`.

## Consequences

- An API change is a four-step routine (update the spec with `AVM_UPDATE_OPENAPI=1`, regenerate, adapt the layer, raise the version); CI rejects a forgotten step.
- Five toolchains in CI (Node, Python, .NET, Go, Rust) plus Docker for generation; the first run of a cold cache builds progenitor.
- A breaking API change needs a SDK major version, and from 2.0 the Go module path needs a `/v2` suffix.
- Browsers cannot call the public deployments directly (the API sends no CORS headers); the TypeScript SDK documents using a proxy.

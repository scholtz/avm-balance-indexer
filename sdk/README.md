# SDKs for the AVM Balance Indexer

Client libraries for the read API of the indexer (`/v1/{chain}/…`): balances at any round, holders, history and token metadata for **Algorand MainNet**, **Algorand TestNet** and **Voi**.
Each SDK is generated from one OpenAPI specification and extended by a small hand-written layer. All five behave the same way:

- **Public deployments are the default.** `algorand-mainnet`, `algorand-testnet` and `voi` talk to `https://mainnet|testnet|voi.balance-indexer.biatec.io` unless you set another base URL.
- **Custom deployments and hosts** (self-hosted, behind your gateway, a local port-forward): set the base URL (and headers, timeout, your own HTTP client).
- One client reads one chain; a deployment serves exactly one chain.
- Amounts are decimal strings, ids and rounds are integers, `kind` is `algo`, `asa` or `arc200`.
- Paging helpers pin the round of the first page; `export holders` handles tokens of any size through a server-side scan.
- Typed errors with status, message, `Retry-After`; no hidden retries.

| Language | Package | Install | Docs | AI instructions |
|---|---|---|---|---|
| TypeScript / JavaScript | `@scholtz/avm-balance-indexer` (npm) | `npm install @scholtz/avm-balance-indexer` | [typescript/README.md](typescript/README.md) | [typescript/AGENTS.md](typescript/AGENTS.md) |
| Python | `avm-balance-indexer` (PyPI) | `pip install avm-balance-indexer` | [python/README.md](python/README.md) | [python/AGENTS.md](python/AGENTS.md) |
| .NET | `AvmBalanceIndexer.Client` (NuGet) | `dotnet add package AvmBalanceIndexer.Client` | [dotnet/README.md](dotnet/README.md) | [dotnet/AGENTS.md](dotnet/AGENTS.md) |
| Go | `github.com/scholtz/avm-balance-indexer/sdk/go` | `go get github.com/scholtz/avm-balance-indexer/sdk/go` | [go/README.md](go/README.md) | [go/AGENTS.md](go/AGENTS.md) |
| Rust | `avm-balance-indexer` (crates.io) | `cargo add avm-balance-indexer` | [rust/README.md](rust/README.md) | [rust/AGENTS.md](rust/AGENTS.md) |

The same five calls in every language (TypeScript shown; the others use the same names in their own casing):

```ts
const indexer = AvmBalanceIndexer.voi();                                  // default URL of the chain, or { baseUrl } for your own
await indexer.status();                                                    // indexed round, lag, ingestion state
await indexer.balance(address, "asa", 31566704, { round: 40_000_000 });    // balance at the end of a round
for await (const h of indexer.iterateHolders("asa", 31566704)) {}          // all holders, consistent round
for await (const e of indexer.iterateHistory(address, "algo", 0)) {}       // effects, newest first
await indexer.token("arc200", 1234567);                                    // symbol, name, decimals, standard
```

## For AI assistants and agents

- [docs/llms.txt](../docs/llms.txt) is a compact, machine-readable description of the service and the SDKs (the `llms.txt` convention).
- Every SDK folder has an `AGENTS.md` with rules to paste into the consuming project, a cheat sheet, recipes and pitfalls.
- [openapi/avm-balance-indexer.v1.json](openapi/avm-balance-indexer.v1.json) is the contract (OpenAPI 3.0); a running service also serves it at `/openapi/v1.json`.

## How the SDKs are built

```
API code ──► sdk/openapi/avm-balance-indexer.v1.json ──► generated code ──► hand-written layer ──► package
              (checked by a test in the API test suite)   (sdk/scripts/generate.sh)
```

| Language | Generator | Generated code (do not edit) |
|---|---|---|
| TypeScript | [openapi-generator](https://openapi-generator.tech) `typescript-fetch` | `typescript/src/generated/` |
| Python | openapi-generator `python` (pydantic v2, urllib3) | `python/src/avm_balance_indexer/_generated/` |
| .NET | [NSwag](https://github.com/RicoSuter/NSwag) (System.Text.Json) | `dotnet/src/AvmBalanceIndexer.Client/Generated/` |
| Go | [oapi-codegen](https://github.com/oapi-codegen/oapi-codegen) | `go/generated.go` |
| Rust | [progenitor](https://github.com/oxidecomputer/progenitor) | `rust/src/generated.rs` |

The generated layers know no deployment (the description names the relative server `/`, see ADR 0015), so their own default address is `http://localhost`: use the hand-written client, which sets the public deployments.

The hand-written layer is `client.ts`, `client.py`, `AvmBalanceIndexerClient.cs`, `client.go` and `lib.rs`; it holds the default URLs, pagination, scan export and error mapping.

### When the API changes

1. Change the API. The test `OpenApiSpecTests` in `tests/AvmBalanceIndexer.Api.Tests` fails because `sdk/openapi/avm-balance-indexer.v1.json` differs from `/openapi/v1.json`.
2. Rewrite the spec: `AVM_UPDATE_OPENAPI=1 dotnet test tests/AvmBalanceIndexer.Api.Tests --filter OpenApiSpecTests` (PowerShell: `$env:AVM_UPDATE_OPENAPI=1; …; Remove-Item Env:AVM_UPDATE_OPENAPI`).
3. Regenerate the SDKs: `sdk/scripts/generate.sh` (needs Docker and the .NET SDK; first run downloads the generator images and builds `cargo-progenitor`, a few minutes). Commit the result.
4. Adapt the hand-written layer and tests if the change needs it, bump the version (below) and describe the change in the pull request.

The workflow `SDKs` regenerates everything in CI and fails when a committed generated file differs, so a forgotten step cannot be merged.

### Versions and publishing

`sdk/VERSION` (MAJOR.MINOR.PATCH) is the version of **all** SDKs. `sdk/scripts/set-version.sh 1.1.0` writes it into every manifest, `sdk/scripts/check-versions.sh` verifies them (CI does).
Raise it in every pull request that changes SDK files other than docs, tests and examples (the `SDKs` check fails otherwise): MAJOR for breaking changes of the SDK surface, MINOR for new features, PATCH for fixes and regenerated code.

When a change under `sdk/` reaches `main`, the workflow `Publish SDKs` runs the tests and publishes every package whose version is not yet in its registry (npm, PyPI, NuGet, crates.io) and tags the Go module.
Setting up the registry tokens is described in [docs/sdk-publishing.md](../docs/sdk-publishing.md).

### Good to know

- A holder export starts a scan on the server before it reads anything. If the request that starts it is aborted or times out after the server accepted it, the SDK never learns the id and cannot cancel it; such a scan ends by itself (see `Scans:Retention` in the README) and counts against `Scans:MaxActive` until then.
- The Rust SDK cannot report the status of an error answer that is not the API's JSON (a gateway's HTML page); it surfaces it as `Error::Decode`, which counts as retryable.

### Tests

Every SDK is tested against a local mock server (no network): default URLs, custom base URL and headers, query serialisation, error mapping, timeouts, pagination with a pinned round and the scan export.
Run them with `npm test`, `pytest`, `dotnet test AvmBalanceIndexer.Client.slnx`, `go test ./...` and `cargo test` in the respective folder. The examples under `examples/` talk to the public TestNet deployment.

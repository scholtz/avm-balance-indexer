# AVM Balance Indexer

Self-hosted indexer and API for Algorand-family (AVM) token balances. Indexes a chain from genesis, follows new
rounds, and serves current and end-of-round balances, holders and per-token account history for native ALGO/VOI,
ASAs, ARC-20 Smart ASAs and ARC-200 application tokens.

Full design: [PRODUCT-DESCRIPTION.md](PRODUCT-DESCRIPTION.md). Decisions: [docs/adr](docs/adr). Running an instance: [docs/operations.md](docs/operations.md).

## Status

| Area | State |
|---|---|
| Native token (ALGO/VOI) indexing from archival nodes | Done. Verified against the ledger for the first 1000 rounds of TestNet and MainNet, and against the public indexer for Voi. |
| PostgreSQL provider | Done: schema, atomic round writer, current and historical balances, holders, history. |
| Cassandra provider | Done: schema, idempotent round writer with compare-and-set publication, current and historical balances, holders, history. Runs the same contract suite as PostgreSQL and is verified against the ledger for the first 1000 rounds of TestNet and MainNet like PostgreSQL. |
| ASA effects (transfer, opt-in/out, close-to, clawback, create, destroy) | Done, verified against the ledger holdings of recent MainNet rounds (`AssetRoundsTests`). ARC-20 Smart ASAs are recognised from chain data and the management addresses follow reconfigurations (`Arc20AssetsTests`); the balance stays an ASA balance. |
| ARC-200 transfers from application logs | Done; 778 transfers of 111 recent Voi rounds match the Voi ARC indexer (`Arc200RoundsTests`). Balances are derived from logged events only. |
| Read API | Done: status, holdings, balance at a round, holders, history, token metadata, asynchronous holder export. Published as an OpenAPI 3.0 document. |
| Client SDKs | Done: TypeScript, Python, .NET, Go and Rust, generated from the OpenAPI document, with the public deployments as defaults ([sdk/](sdk/README.md)). |
| Voi | Genesis pinned and verified; first 1000 rounds indexed (no balance-moving transactions) and a 2500-round recent window with real payments checked against the public indexer (`VoiRecentRoundsTests`). Voi serves no ledger deltas, so a proposer-payout or hidden-rewards check as on Algorand is not possible. |

## How indexing works

1. **Block source.** Blocks come from archival gossip nodes, discovered through the network DNS SRV record with the
   [Algorand5](https://www.nuget.org/packages/Algorand5) SDK, or from nodes you configure. Fetching adds host failover,
   timeouts, backoff and validation on top of the SDK client. Rounds near the chain tip (`Algorand:RecentRoundsWindow`, 100) are
   also fetched from relays, which are far more numerous than archival nodes. The latest round comes from an algod node.
2. **Effects.** Each block is turned into exact, ordered native balance changes: payments, fees, realized rewards,
   close-outs, inner transactions, proposer payouts, the per-round rewards pool withdrawal and known ledger hotfixes.
3. **Trust.** The genesis file served by algod must hash to the pinned genesis hash, and every block header must hash to its
   certificate digest and link to the previous block, and its transactions must reproduce the header payset commitments. The
   starting balances and the blocks therefore belong to one chain (trust model: [ADR 0004](docs/adr/0004-block-trust-model.md)).
4. **Storage.** The ordered indexer computes the running balance after each effect and commits a whole round in one
   PostgreSQL transaction together with the publication cursor. A block must link to the previously published block hash.
5. **Resume.** Restarting continues from the stored cursor and re-checks the cursor block against the archival source.

## Repository layout

```
src/
  AvmBalanceIndexer.Domain/              token/effect model, exact Amount, storage contract interfaces
  AvmBalanceIndexer.Indexing/            gossip block fetcher, block decoding, effect normalization, round indexer
  AvmBalanceIndexer.Storage.PostgreSql/  provider + Schema/*.sql
  AvmBalanceIndexer.Storage.Cassandra/   provider + Schema/*.cql
  AvmBalanceIndexer.Hosting/             shared DI and configuration wiring
  AvmBalanceIndexer.Api/                 ASP.NET Core /v1/{chain} read API
  AvmBalanceIndexer.Worker/              indexer worker (single ordered writer per chain)
sdk/                                     client SDKs (typescript, python, dotnet, go, rust), OpenAPI document, generator script
tests/
  AvmBalanceIndexer.Domain.Tests/        domain unit tests
  AvmBalanceIndexer.Indexing.Tests/      decoder, fetcher, normalizer, rewards pool, tracker unit tests
  AvmBalanceIndexer.Hosting.Tests/       configuration and DI wiring tests
  AvmBalanceIndexer.Worker.Tests/        worker retry, progress and fail-fast behaviour
  AvmBalanceIndexer.Storage.ContractTests/ one contract suite run against the in-memory reference and PostgreSQL
  AvmBalanceIndexer.Live.Tests/          first 1000 rounds of real chains checked against the ledger (needs internet)
  AvmBalanceIndexer.TestSupport/         in-memory provider, synthetic chain, shared contract suite
deploy/helm/                             chart + stage-testnet / prod-algorand / prod-voi overlays
docker/                                  Dockerfile.api, Dockerfile.worker
docs/adr/                                architecture decision records
```

## Getting started

Requires the .NET 10 SDK and Docker.

```sh
docker compose up -d postgres
dotnet build AvmBalanceIndexer.slnx

# Index Algorand TestNet into PostgreSQL (starts from genesis, follows the tip)
export Chain=algorand-testnet
export Storage__Provider=PostgreSql
export "Storage__PostgreSql__ConnectionString=Host=localhost;Port=5432;Database=avm_indexer;Username=avm;Password=avm"
dotnet run --project src/AvmBalanceIndexer.Worker

# In another shell: serve the status
dotnet run --project src/AvmBalanceIndexer.Api
curl http://localhost:5000/v1/algorand-testnet/status
```

### Configuration

| Key | Meaning |
|---|---|
| `Chain` | `algorand-mainnet`, `algorand-testnet` or `voi`. One process serves one chain. |
| `Storage:Provider` | `PostgreSql` or `Cassandra`. Only the selected provider is opened. |
| `Storage:PostgreSql:ConnectionString` | Required for PostgreSQL. Keep it in a secret. |
| `Storage:PostgreSql:AccountHashPartitions` | Hash partitions of the account effect table, 1 to 1024. Fixed per database. |
| `Storage:PostgreSql:ReadTimeoutSeconds` / `Storage:Cassandra:ReadTimeoutSeconds` | Default 30. PostgreSQL: `statement_timeout` of each read query. Cassandra: deadline of a whole read and driver timeout of every request (at least 5). A slower read fails with HTTP 503 instead of holding a snapshot. |
| `Storage:Cassandra:WriteConcurrency` | Requests the round writer keeps in flight (256). |
| `Storage:Cassandra:LeaseTtlSeconds` / `Storage:PostgreSql:LeaseHeartbeatSeconds` | Writer lease tuning (default 30 / 5): the Cassandra lease row TTL bounds failover time; the PostgreSQL heartbeat detects a dead lease session. |
| `Storage:Cassandra:ContactPoints` / `Port` | Required for Cassandra: hosts and native-protocol port (default 9042). |
| `Storage:Cassandra:Keyspace` / `LocalDatacenter` | One keyspace per chain deployment; the datacenter the driver routes to. Reads and writes use `LOCAL_QUORUM`. |
| `Storage:Cassandra:CreateKeyspace` / `ReplicationFactor` | Let the worker create the keyspace (`NetworkTopologyStrategy`, default 3 replicas). Use `ReplicationFactor` 1 on a single-node dev cluster (reads and writes need a `LOCAL_QUORUM`). Set `CreateKeyspace` to false when the operator manages it. |
| `Storage:Cassandra:Username` / `Password` / `UseTls` | Optional credentials (keep them in a secret) and TLS. Without `UseTls` credentials travel in clear text. |
| `Algorand:AlgodUrl` / `AlgodToken` | Algod node for the latest round and genesis. Defaults to a public endpoint. |
| `Algorand:GossipHosts` | Archival gossip node URLs. Empty discovers them through DNS SRV. Use your own nodes in production. |
| `Algorand:RecentRoundsWindow` | While following the tip, rounds within this distance of it are fetched from DNS-discovered relays as well as archival nodes (default 100, 0 = archival only; ignored when `GossipHosts` is set). Older rounds never go to relays, which prune old blocks (issue #141). |
| `Algorand:MaxConcurrentRequestsPerHost` / `MaxThrottleDelay` / `ThrottleTimeout` | Politeness towards the archival gossip nodes (issue #120). Requests in flight per node (default 32): the limit adapts, it halves whenever a node answers `429` and grows back with successes, and a request that finds a node full or paused goes to the next one. A `429` (or `503` with `Retry-After`) pauses the node for its `Retry-After` (seconds or date) or, without one, for 1 s doubling per pause, at most `MaxThrottleDelay` (60 s); it does not use up `Algorand:MaxAttempts`. A fetch that is rate limited for `ThrottleTimeout` (5 min) fails as a transient failure, so the worker retries with its usual back-off. The log shows one information summary per `Algorand:ThrottleLogInterval` (1 min; nodes, pauses, limits) instead of a line per pause, which is logged at debug level. After a `429` the limit grows slowly near the level that caused it. |
| `Algorand:ExpectedGenesisHash` | Base64 genesis hash. Pinned for Algorand MainNet, TestNet and Voi; set it only for another network. |
| `Serilog:*` / `Logging:Format` | Logging (issue #126, ADR 0018). Levels and sinks come from the `Serilog` section (`Serilog__MinimumLevel__Default=Debug`, `Serilog__MinimumLevel__Override__Microsoft.AspNetCore=Information`, or `Serilog:WriteTo` for other sinks); `Logging:LogLevel` is not used. `Logging:Format=Json` writes one compact JSON object per line (Helm `logging.format`, default `json`; `logging.level` sets the default level), every event carries `Service` and `Chain`. The API logs one line per request (health probes at Debug). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` / `Telemetry:Enabled` | Publishes metrics over OTLP from API and worker (ASP.NET Core, .NET runtime, `AvmBalanceIndexer.Worker` instruments) when an endpoint is set; `Telemetry:Enabled=false` forces it off. The other `OTEL_*` variables apply. |
| `Api:RateLimitPerMinute` / `RequestTimeoutSeconds` / `TrustForwardedHeaders` / `PublishOpenApi` / `PathBase` | API protection: requests per client IP and minute (600, 0 = off; `429` + `Retry-After`), request timeout (60 s, `504`), client address from `X-Forwarded-For` (off; enable behind your ingress, with `KnownProxyNetworks` and `ForwardedHeadersLimit` if the API can also be reached directly or through several hops; IPv6 clients are limited per /64), OpenAPI at `/openapi/v1.json` and a Swagger UI at `/swagger` (the root `/` redirects there; both on, also in production; off, the root answers a small JSON landing document; `PathBase` for an ingress that forwards a path prefix). The description names a relative server (`/`, or the path base) so the UI works behind any ingress; set the server address yourself when you generate a client from a downloaded copy. The Swagger UI files have their own per-client window of `max(RateLimitPerMinute, 60)` requests a minute (a page load is about ten requests); 0 turns both off. Responses are gzip/brotli compressed when accepted. |
| `Scans:MaxActive` / `Retention` / `Concurrency` / `PollInterval` / `StaleAfter` / `PageSize` | Asynchronous holder scans: admission limit (20), how long a scan and its result are kept (24 h), scans one worker replica runs at once (2), how often an idle worker looks for work (5 s), when a silent owner loses a running scan (2 min), holders per step (1000). |
| `Indexer:LeaseRetryInterval` | How often a standby replica asks again for the writer lease (default 5 s). |
| `Worker:HealthPort` / `HealthHost` | Worker probe endpoints `/healthz/live` and `/healthz/ready` (default 8081, 0 = off; use `localhost` as host on Windows). Metrics are on the `AvmBalanceIndexer.Worker` meter. |
| `Indexer:FetchConcurrency` | Blocks fetched in parallel ahead of the commit position (default 128; each block in flight is held decoded, typically 20 to 100 KB). Downloading from the gossip hosts is what limits a backfill: about 17 rounds/s at 8, 30 at 32 and 60 to 70 at 96 or more on busy MainNet blocks, 100 to 160 on tiny early blocks. The fetcher prefers the archival hosts that answer fastest (they differ by 10x); your own archival nodes remove the limit. |
| `Indexer:CommitBatchRounds` / `CommitBatchEffects` | While backfilling, up to this many consecutive rounds (default 25) or effects (default 20,000) are written in one transaction; near the tip every round is committed alone. 1 turns batching off. |

## Tests

```sh
dotnet test AvmBalanceIndexer.slnx                    # everything; PostgreSQL and Cassandra run in Testcontainers containers (Cassandra takes about a minute to start)
AVM_SKIP_LIVE_TESTS=1 dotnet test AvmBalanceIndexer.slnx   # without the network tests
AVM_TEST_POSTGRES="Host=...;Database=...;Username=...;Password=..." dotnet test ...   # use an existing server instead
AVM_TEST_CASSANDRA=localhost:9042 AVM_TEST_CASSANDRA_DC=dc1 dotnet test ...             # same for Cassandra
```

The live tests fetch the first 1000 rounds of TestNet, MainNet and Voi from public archival nodes and index them. For
TestNet and MainNet, into both providers, they compare for every round the balance of every account the ledger changed with
the ledger state delta from a public algod. Voi publishes no historical deltas, so it is checked against a public indexer.
They take a few minutes.

## API (v1)

| Endpoint | Purpose | State |
|---|---|---|
| `GET /v1/{chain}/status` | indexed round, target round, lag, ingestion state | available |
| `GET /v1/{chain}/accounts/{address}/tokens` | current holdings | available |
| `GET /v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/balance?round=R` | end-of-round balance | available |
| `GET /v1/{chain}/tokens/{kind}/{tokenId}/holders?round=R` | paginated end-of-round holders | available |
| `GET /v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/transactions` | account/token effect history | available |
| `GET /v1/{chain}/tokens/{kind}/{tokenId}` | token metadata | available |
| `POST /v1/{chain}/tokens/{kind}/{tokenId}/holders/scans` | start an asynchronous holder export (`{ "round": R, "includeZero": false }`) | available |
| `GET /v1/{chain}/scans/{id}` / `GET .../scans/{id}/holders` / `DELETE .../scans/{id}` | poll, read the result pages, cancel | available |

Amounts and deltas are decimal strings. Every list response has `indexedRound` (as of the response) and `nextCursor` (opaque; pass it back as
`cursor`). `limit` is 1 to 1000 (default 100); `kind` is `algo`, `asa` or `arc200`. Without `round`, balance and holders use
the latest published round; an explicit published round has a fixed answer, so those responses carry `Cache-Control: public, max-age=3600`.
Scan results carry the scan's `round` instead of `indexedRound`. While the scan runs, a page that has caught up is empty and carries the same `nextCursor`; with no `nextCursor` and `complete` false, repeat
the same request (same cursor) a moment later: the scan is still running. Asynchronous scans: `POST` answers `202` with a `Location`; the job reports `state` (`Pending`, `Running`, `Completed`, `Failed`,
`Cancelled`), `holdersFound` and `error`. The result is a consistent list of the holders at the scan's round, in account order; pages
can be read while the scan runs (`complete` is true only for a finished scan and its last page). Every worker replica runs scans; a
scan whose replica dies is resumed from its checkpoint by another one. `429` with `Retry-After` when too many scans are active.
Errors: 400 for invalid input or cursor, 404 for an unknown token, a round beyond the published one (with `indexedRound`) or
another chain, 503 while nothing is indexed yet. A walk over the holders of the latest round should pass the
`round` it received, so that all pages come from one round.

## Client SDKs

Use the API from your own code with a typed client; each SDK defaults to the public deployments of Algorand MainNet, Algorand TestNet and Voi and accepts a custom base URL for your own deployment.

| Language | Install | Docs |
|---|---|---|
| TypeScript | `npm install @scholtz/avm-balance-indexer` | [sdk/typescript](sdk/typescript/README.md) |
| Python | `pip install avm-balance-indexer` | [sdk/python](sdk/python/README.md) |
| .NET | `dotnet add package AvmBalanceIndexer.Client` | [sdk/dotnet](sdk/dotnet/README.md) |
| Go | `go get github.com/scholtz/avm-balance-indexer/sdk/go` | [sdk/go](sdk/go/README.md) |
| Rust | `cargo add avm-balance-indexer` | [sdk/rust](sdk/rust/README.md) |

```ts
const indexer = AvmBalanceIndexer.voi();                                // or algorandMainnet(), algorandTestnet(), { baseUrl: "https://my-indexer" }
const { amount } = await indexer.balance(address, "asa", 31566704, { round: 40_000_000 });
```

The clients are generated from [sdk/openapi/avm-balance-indexer.v1.json](sdk/openapi/avm-balance-indexer.v1.json), the OpenAPI document of the API (kept equal to `/openapi/v1.json` by a test, regenerated and diffed in CI).
AI assistants: every SDK has an `AGENTS.md`, and [docs/llms.txt](docs/llms.txt) summarises the service. Overview, regeneration and versioning: [sdk/README.md](sdk/README.md); publishing and its GitHub secrets: [docs/sdk-publishing.md](docs/sdk-publishing.md).

## PostgreSQL or Cassandra?

Both providers pass the same contract suite and produce identical data, so the choice is about cost and read patterns. The numbers below come
from `tools/AvmBalanceIndexer.Benchmark` and are **re-measured every night** by the workflow
[Nightly benchmark](https://github.com/scholtz/avm-balance-indexer/actions/workflows/benchmark.yml)
([![Nightly benchmark](https://github.com/scholtz/avm-balance-indexer/actions/workflows/benchmark.yml/badge.svg)](https://github.com/scholtz/avm-balance-indexer/actions/workflows/benchmark.yml)).
**Latest results:** the [`benchmark-results` branch](https://github.com/scholtz/avm-balance-indexer/tree/benchmark-results)
([latest.md](https://github.com/scholtz/avm-balance-indexer/blob/benchmark-results/latest.md), one JSON file per night in `history/`) and
the summary page of each workflow run. Method, caveats and how to run it yourself: [docs/benchmarks.md](docs/benchmarks.md).

The tables are a snapshot from the day the benchmark was added; the nightly run measures both chains.

### Algorand MainNet, 10,000 newest rounds

Measured on 2026-10-11 at commit `f6a47219`: the 10,000 newest rounds of algorand-mainnet (chain rounds 65,882,216 to 65,892,215) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Microsoft Windows 10.0.26200, 24 cores, 64 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 47 rounds/s | 80 rounds/s |
| Effects written | 992,276 (4,630/s) | 992,276 (7,966/s) |
| Time for the range | 214.3 s | 124.6 s |
| Stored size | 156.9 MiB | 650.5 MiB |
| Per round | 16.06 KiB | 66.61 KiB |
| Per effect | 163 bytes | 677 bytes |
| If all 66 M MainNet rounds were like these | 1.0 TiB | 4.1 TiB |

Block download is the same for both and not part of the table: 58 rounds/s from the public archival nodes (260,035 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 81.1 MiB, effects_by_token_bucket 63.8 MiB, effect_buckets_by_account_token 4.5 MiB, holdings_by_token 2.3 MiB, accounts_touched_by_token 2.2 MiB
- PostgreSQL 17.11: effects_by_account_token 354.8 MiB, effects_by_token_bucket 265.2 MiB, holdings_by_token 9.4 MiB, accounts_touched_by_token 9.4 MiB, holdings_by_account 8.7 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 8 ARC-200 tokens, 7,066 ASAs. 3,120 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | ALGO | 678,650 | 5,038 | 15,692 |
| asa:3145862805 |  | 18,281 | 2,282 | 2,283 |
| asa:849191641 |  | 29,549 | 1,505 | 7,063 |
| asa:31566704 |  | 69,153 | 786 | 4,659 |
| asa:3203964481 |  | 3,692 | 95 | 125 |
| asa:386192725 |  | 4,046 | 74 | 99 |
| asa:1284444444 |  | 6,388 | 19 | 39 |
| asa:127746157 |  | 51,666 | 0 | 1 |
| asa:127745593 |  | 32,793 | 0 | 1 |
| asa:127746786 |  | 17,855 | 0 | 1 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.8 / 1.1 / 1.8 ms; 12,792 req/s, p95 2 ms | 0.6 / 0.7 / 1.2 ms; 10,677 req/s, p95 2 ms |
| account holdings | 1.6 / 2.0 / 3.0 ms; 4,199 req/s, p95 14 ms | 1.9 / 2.5 / 3.1 ms; 4,127 req/s, p95 5 ms |
| balance, latest | 2.7 / 3.2 / 3.5 ms; 2,636 req/s, p95 21 ms | 2.7 / 3.2 / 3.5 ms; 3,316 req/s, p95 6 ms |
| balance, past round | 2.6 / 3.2 / 3.8 ms; 2,755 req/s, p95 22 ms | 2.4 / 3.1 / 3.6 ms; 3,495 req/s, p95 6 ms |
| history, busiest account | 3.7 / 4.5 / 5.2 ms; 1,779 req/s, p95 15 ms | 3.4 / 4.4 / 4.7 ms; 1,791 req/s, p95 14 ms |
| history, typical account | 1.9 / 2.3 / 2.8 ms; 3,898 req/s, p95 16 ms | 2.2 / 2.7 / 3.1 ms; 4,191 req/s, p95 5 ms |
| holders, native, latest | 13.5 / 15.3 / 17.7 ms; 299 req/s, p95 111 ms | 3.3 / 4.0 / 21.1 ms; 2,049 req/s, p95 10 ms |
| holders, native, past round | 72.5 / 123.3 / 133.4 ms; 106 req/s, p95 192 ms | 6.7 / 7.4 / 7.8 ms; 931 req/s, p95 21 ms |
| holders, busiest ASA, latest | 31.9 / 36.8 / 46.6 ms; 172 req/s, p95 134 ms | 3.2 / 3.8 / 4.2 ms; 2,165 req/s, p95 9 ms |
| holders, busiest ARC-200, latest | 2.1 / 2.3 / 2.6 ms; 2,768 req/s, p95 17 ms | 2.0 / 2.6 / 6.2 ms; 3,853 req/s, p95 7 ms |
| token info | 0.7 / 0.9 / 1.1 ms; 12,051 req/s, p95 2 ms | 0.7 / 0.9 / 0.9 ms; 10,795 req/s, p95 2 ms |

### Voi, 100,000 newest rounds

Voi is a quiet chain: 100,000 rounds hold only a few thousand transactions, so this shows what the many near-empty rounds of a backfill cost.

Measured on 2026-10-11 at commit `f6a47219`: the 100,000 newest rounds of voi (chain rounds 23,380,220 to 23,480,219) were indexed from an empty database by the production indexer. Both databases indexed exactly this range, each running alone next to the API on Microsoft Windows 10.0.26200, 24 cores, 64 GiB.

#### Indexing and storage

| | Cassandra 5.0.9 | PostgreSQL 17.11 |
|---|---:|---:|
| Indexing speed | 4,424 rounds/s | 6,119 rounds/s |
| Effects written | 17,932 (793/s) | 17,932 (1,097/s) |
| Time for the range | 22.6 s | 16.3 s |
| Stored size | 2.4 MiB | 14.4 MiB |
| Per round | 0.02 KiB | 0.15 KiB |
| Per effect | 138 bytes | 835 bytes |

Block download is the same for both and not part of the table: 74 rounds/s from the public archival nodes (7,329 transactions).

#### Largest tables

- Cassandra 5.0.9: effects_by_account_token_bucket 1.3 MiB, effects_by_token_bucket 0.9 MiB, effect_buckets_by_account_token 0.0 MiB, holdings_by_token 0.0 MiB, accounts_touched_by_token 0.0 MiB
- PostgreSQL 17.11: effects_by_account_token 9.0 MiB, effects_by_token_bucket 4.8 MiB, holdings_by_token 0.1 MiB, accounts_touched_by_token 0.1 MiB, holdings_by_account 0.1 MiB

#### What the range contains

Tokens with at least one effect in the range: 1 native, 26 ARC-200 tokens, 15 ASAs. 0 of those ASAs were created inside the range, 0 of them ARC-20 Smart ASAs.

Holders are counted among the accounts that had an effect in the range (plus the starting balances the replay needed); a token with a long history has more holders on the real chain than shown here. Names exist only for tokens created inside the range.

| Token | Name | Effects | Holders (non-zero) | Opted in (incl. zero) |
|---|---|---:|---:|---:|
| algo:0 | VOI | 14,814 | 45 | 78 |
| arc200:300279 |  | 110 | 20 | 24 |
| arc200:420069 |  | 236 | 18 | 26 |
| arc200:404004 |  | 86 | 16 | 21 |
| arc200:411311 |  | 70 | 15 | 16 |
| arc200:390001 |  | 592 | 8 | 25 |
| arc200:47138068 |  | 81 | 6 | 11 |
| arc200:395614 |  | 224 | 5 | 14 |
| asa:302190 |  | 136 | 5 | 9 |
| asa:586896 |  | 948 | 1 | 2 |

#### API latency

One request at a time (p50 / p95 / p99), then 16 requests at a time (throughput and p95). The API runs as its own process on the same machine, so network time is not included.

| Request | cassandra | postgres |
|---|---:|---:|
| status | 0.8 / 1.4 / 2.5 ms; 10,190 req/s, p95 2 ms | 0.7 / 0.8 / 1.5 ms; 10,293 req/s, p95 2 ms |
| account holdings | 1.6 / 2.4 / 2.8 ms; 3,848 req/s, p95 15 ms | 2.0 / 3.0 / 4.2 ms; 3,984 req/s, p95 5 ms |
| balance, latest | 2.9 / 4.0 / 4.9 ms; 2,301 req/s, p95 24 ms | 2.9 / 3.9 / 6.3 ms; 2,696 req/s, p95 11 ms |
| balance, past round | 2.9 / 4.4 / 6.4 ms; 2,753 req/s, p95 20 ms | 2.7 / 3.6 / 4.4 ms; 3,225 req/s, p95 7 ms |
| history, busiest account | 5.3 / 6.7 / 7.7 ms; 1,445 req/s, p95 21 ms | 3.7 / 6.1 / 10.6 ms; 1,870 req/s, p95 13 ms |
| history, typical account | 2.1 / 3.6 / 4.4 ms; 2,779 req/s, p95 18 ms | 2.3 / 3.8 / 4.9 ms; 3,642 req/s, p95 7 ms |
| holders, native, latest | 3.7 / 4.8 / 5.7 ms; 1,340 req/s, p95 31 ms | 2.8 / 3.5 / 3.8 ms; 2,716 req/s, p95 8 ms |
| holders, native, past round | 16.1 / 31.3 / 36.1 ms; 494 req/s, p95 54 ms | 5.8 / 6.9 / 8.3 ms; 1,099 req/s, p95 20 ms |
| holders, busiest ASA, latest | 2.3 / 2.9 / 3.2 ms; 2,228 req/s, p95 24 ms | 2.1 / 2.8 / 3.6 ms; 4,197 req/s, p95 5 ms |
| holders, busiest ARC-200, latest | 2.5 / 3.1 / 3.5 ms; 2,025 req/s, p95 21 ms | 2.3 / 3.0 / 3.3 ms; 3,702 req/s, p95 6 ms |
| token info | 0.8 / 1.1 / 1.5 ms; 14,617 req/s, p95 1 ms | 0.7 / 1.0 / 2.2 ms; 10,535 req/s, p95 2 ms |

What it says, in short (MainNet): PostgreSQL answers the holder queries (a page of the biggest token's holders, at the latest or at a past round) about 4 to 11 times faster
and indexes about 1.7 times faster on one node; Cassandra stores the same data in about a quarter of the space (compression, no secondary
indexes) and is the one to pick when you need to scale writes and storage across nodes. Account queries (holdings, balances, history) are within
a few milliseconds of each other (PostgreSQL has the lower tail at 16 concurrent requests). Voi shows the same picture on a smaller scale: 6 times
less disk on Cassandra, the same ranking, with the differences small on such a quiet chain. Caveats: both databases ran as one local node (Cassandra with replication factor 1: a real cluster stores
three copies and pays quorum latency); the full-chain size is the 10,000-round figure scaled up, an upper bound because older rounds carry less activity.

## Known limitations

- Holders at the latest round are one ordered index scan. Holders at an older round read each holder's latest balance from its
  own history, one lookup per account, so a page is cheap but a huge token at a past round can exceed the read timeout
  (HTTP 503): use an asynchronous scan for it.
- Backfill speed is limited by downloading blocks from gossip hosts (about 100 ms per block and host, 25 to 60 rounds/s on
  busy MainNet blocks with 32 to 192 in flight; empty early blocks are much faster). The database is no longer the limit:
  committing 25 rounds per transaction raised the PostgreSQL write path from about 330 to about 3,900 rounds/s in the benchmark (`BackfillBenchmark`, `AVM_BENCH=1`). Cassandra saves one compare-and-set per batch
  instead of per round: on one local node (Docker, replication factor 1, no network latency between worker and database) 364 rounds/s with one round per commit,
  1,900 with 25 and 2,500 with 100 for early TestNet rounds, and about 12,000 effects per second for synthetic busy rounds with 256 requests in flight
  (`CassandraBackfillBenchmark`, `Storage:Cassandra:WriteConcurrency`; 64 gave about 8,000). A replicated cluster over a real network will differ: measure with your hardware. Running several chains or your own archival nodes is the way to go faster.
- Several workers may run per chain: one holds the writer lease (PostgreSQL advisory lock on a dedicated session, Cassandra TTL row) and indexes, the others stand by. Failover takes up to the lease TTL (30 s) on Cassandra and about 30 s (TCP keepalives of the lease session) on PostgreSQL. Worker metrics are published on the `AvmBalanceIndexer.Worker` meter and exported over OTLP when an endpoint is configured.
- Block headers are hash-checked against their certificate digest and transactions against the header commitments, but
  certificate signatures are not verified (ADR 0004). A host serving a whole fabricated chain is not detected: run your
  own archival nodes.
- Proposer payouts are verified against recent MainNet rounds (see `docs/ledger-facts.md`).
- A database indexed before ASA support (#18) holds no asset history: rebuild it from genesis, otherwise the first transfer of an older asset fails with a negative balance. Application-driven native movements are only seen through inner payments.

## License

[MIT](LICENSE)

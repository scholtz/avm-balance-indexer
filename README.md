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
| Read API | Done: status, holdings, balance at a round, holders, history, token metadata. |
| Voi | Genesis pinned and verified; first 1000 rounds indexed (no balance-moving transactions) and a 2500-round recent window with real payments checked against the public indexer (`VoiRecentRoundsTests`). Voi serves no ledger deltas, so a proposer-payout or hidden-rewards check as on Algorand is not possible. |

## How indexing works

1. **Block source.** Blocks come from archival gossip nodes, discovered through the network DNS SRV record with the
   [Algorand5](https://www.nuget.org/packages/Algorand5) SDK, or from nodes you configure. Fetching adds host failover,
   timeouts, backoff and validation on top of the SDK client. The latest round comes from an algod node.
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
| `Storage:Cassandra:LeaseTtlSeconds` / `Storage:PostgreSql:LeaseHeartbeatSeconds` | Writer lease tuning (default 30 / 5): the Cassandra lease row TTL bounds failover time; the PostgreSQL heartbeat detects a dead lease session. |
| `Storage:Cassandra:ContactPoints` / `Port` | Required for Cassandra: hosts and native-protocol port (default 9042). |
| `Storage:Cassandra:Keyspace` / `LocalDatacenter` | One keyspace per chain deployment; the datacenter the driver routes to. Reads and writes use `LOCAL_QUORUM`. |
| `Storage:Cassandra:CreateKeyspace` / `ReplicationFactor` | Let the worker create the keyspace (`NetworkTopologyStrategy`, default 3 replicas). Use `ReplicationFactor` 1 on a single-node dev cluster (reads and writes need a `LOCAL_QUORUM`). Set `CreateKeyspace` to false when the operator manages it. |
| `Storage:Cassandra:Username` / `Password` / `UseTls` | Optional credentials (keep them in a secret) and TLS. Without `UseTls` credentials travel in clear text. |
| `Algorand:AlgodUrl` / `AlgodToken` | Algod node for the latest round and genesis. Defaults to a public endpoint. |
| `Algorand:GossipHosts` | Archival gossip node URLs. Empty discovers them through DNS SRV. Use your own nodes in production. |
| `Algorand:ExpectedGenesisHash` | Base64 genesis hash. Pinned for Algorand MainNet, TestNet and Voi; set it only for another network. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` / `Telemetry:Enabled` | Publishes metrics over OTLP from API and worker (ASP.NET Core, .NET runtime, `AvmBalanceIndexer.Worker` instruments) when an endpoint is set; `Telemetry:Enabled=false` forces it off. The other `OTEL_*` variables apply. |
| `Api:RateLimitPerMinute` / `RequestTimeoutSeconds` / `TrustForwardedHeaders` / `PublishOpenApi` | API protection: requests per client IP and minute (600, 0 = off; `429` + `Retry-After`), request timeout (60 s, `504`), client address from `X-Forwarded-For` (off; enable behind your ingress, with `KnownProxyNetworks` and `ForwardedHeadersLimit` if the API can also be reached directly or through several hops; IPv6 clients are limited per /64), OpenAPI at `/openapi/v1.json` (on). Responses are gzip/brotli compressed when accepted. |
| `Scans:MaxActive` / `Retention` / `Concurrency` / `PollInterval` / `StaleAfter` / `PageSize` | Asynchronous holder scans: admission limit (20), how long a scan and its result are kept (24 h), scans one worker replica runs at once (2), how often an idle worker looks for work (5 s), when a silent owner loses a running scan (2 min), holders per step (1000). |
| `Indexer:LeaseRetryInterval` | How often a standby replica asks again for the writer lease (default 5 s). |
| `Worker:HealthPort` / `HealthHost` | Worker probe endpoints `/healthz/live` and `/healthz/ready` (default 8081, 0 = off; use `localhost` as host on Windows). Metrics are on the `AvmBalanceIndexer.Worker` meter. |
| `Indexer:FetchConcurrency` | Blocks fetched in parallel ahead of the commit position (default 64; each block in flight is held decoded, typically 20 to 100 KB). Downloading from the gossip hosts is what limits a backfill: about 9 rounds/s at 8, 25 at 32 and 45 to 60 at 192 on busy MainNet blocks. |
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
Scan results carry the scan's `round` instead of `indexedRound`. If a result page has no `nextCursor` and `complete` is false, repeat
the same request (same cursor) a moment later: the scan is still running. Asynchronous scans: `POST` answers `202` with a `Location`; the job reports `state` (`Pending`, `Running`, `Completed`, `Failed`,
`Cancelled`), `holdersFound` and `error`. The result is a consistent list of the holders at the scan's round, in account order; pages
can be read while the scan runs (`complete` is true only for a finished scan and its last page). Every worker replica runs scans; a
scan whose replica dies is resumed from its checkpoint by another one. `429` with `Retry-After` when too many scans are active.
Errors: 400 for invalid input or cursor, 404 for an unknown token, a round beyond the published one (with `indexedRound`) or
another chain, 503 while nothing is indexed yet. A walk over the holders of the latest round should pass the
`round` it received, so that all pages come from one round.

## Known limitations

- Holders at the latest round are one ordered index scan. Holders at an older round read each holder's latest balance from its
  own history, one lookup per account, so a page is cheap but a huge token at a past round can exceed the read timeout
  (HTTP 503): use an asynchronous scan for it.
- Backfill speed is limited by downloading blocks from gossip hosts (about 100 ms per block and host, 25 to 60 rounds/s on
  busy MainNet blocks with 32 to 192 in flight; empty early blocks are much faster). The database is no longer the limit:
  committing 25 rounds per transaction raised the PostgreSQL write path (Cassandra saves one compare-and-set per batch instead of per round; not benchmarked) from about 330 to about 3,900 rounds/s in the benchmark
  (`BackfillBenchmark`, `AVM_BENCH=1`). Running several chains or your own archival nodes is the way to go faster.
- Several workers may run per chain: one holds the writer lease (PostgreSQL advisory lock on a dedicated session, Cassandra TTL row) and indexes, the others stand by. Failover takes up to the lease TTL (30 s) on Cassandra and about 30 s (TCP keepalives of the lease session) on PostgreSQL. Worker metrics are published on the `AvmBalanceIndexer.Worker` meter and exported over OTLP when an endpoint is configured.
- Block headers are hash-checked against their certificate digest and transactions against the header commitments, but
  certificate signatures are not verified (ADR 0004). A host serving a whole fabricated chain is not detected: run your
  own archival nodes.
- Proposer payouts are verified against recent MainNet rounds (see `docs/ledger-facts.md`).
- A database indexed before ASA support (#18) holds no asset history: rebuild it from genesis, otherwise the first transfer of an older asset fails with a negative balance. Application-driven native movements are only seen through inner payments.

## License

[MIT](LICENSE)

# Algorand Family Asset Indexer: PostgreSQL, Cassandra, and Kubernetes Design

## Product description

Build a self-hosted API that indexes each supported chain from genesis, then continuously follows new rounds. It provides:

- Current token balances and holdings by account.
- Account balance for a token at the end of any indexed round.
- Token holders at the end of any indexed round, with an optional view of opted-in accounts holding zero.
- Account transaction/effect history for a specific token.
- Support for native ALGO, ASAs, ARC-20 Smart ASAs, and ARC-200 application tokens.

Every deployment is on Kubernetes. Stage indexes Algorand TestNet. Production indexes Voi and Algorand MainNet as separate chain deployments with separate databases. Each chain has its own ingestion cursor, API, and storage domain so a reindex or failure on one chain cannot corrupt another.

“Balance at round” means the end-of-round balance. Intra-round state queries are out of scope initially.

## Dual database support and configuration

Ship two working storage providers: PostgreSQL and Apache Cassandra. The API, chain adapters, effect normalization, and endpoint behavior are shared. Each provider implements the same storage contract for token metadata, current holdings, ordered effects, touched-account lookup, round status, replay, and historical balance/holder reads. Use provider-specific schemas and query implementations; do not try to force a lowest-common-denominator ORM schema across both databases.

Select one provider per chain deployment at startup. Stage TestNet, production Algorand, and production Voi may each choose independently, allowing benchmark results to determine the final choice. Example configuration:

```json
{
  "Storage": {
    "Provider": "PostgreSql",
    "PostgreSql": { "ConnectionString": "from-secret" },
    "Cassandra": {
      "ContactPoints": ["cassandra-0.cassandra", "cassandra-1.cassandra"],
      "Keyspace": "algorand_indexer",
      "LocalDatacenter": "dc1",
      "Username": "from-secret",
      "Password": "from-secret"
    }
  }
}
```

Only the selected provider is opened by the service. Store credentials in Kubernetes Secrets and non-secret settings in Helm values/ConfigMaps. Switching providers requires redeploying with the new setting and indexing/replaying the chain into that provider’s database; do not dual-write production traffic to both backends. Keep a backend-neutral immutable source (archived blocks or canonical effects) for repeatable data loads and fair benchmarking. One API image can support both providers; package provider dependencies in the image and validate each provider in CI and deployment smoke tests.

The shared contract must guarantee the same exact amount encoding, end-of-round semantics, deterministic effect identity, idempotent replay, pagination behavior, and publication rule: an API must never expose a round until all required rows for that round are committed in that provider.

Definition of done for dual support: CI starts both database engines, applies each provider’s schema, replays the same fixture rounds into both, and runs the same repository/API contract suite against each. The suite compares current balances, historical balances (including rounds with no account activity), holder pages, zero-balance opt-ins, history ordering, duplicate replay, and round publication behavior. Kubernetes smoke tests deploy the same API/indexer image once with PostgreSQL configuration and once with Cassandra configuration. Provider selection must require configuration only, not a code rebuild or endpoint change.

## Token semantics

ARC-20 is **Smart ASA**, an ASA controlled by a smart contract. It keeps the underlying ASA identity, opt-in model, and on-ledger ASA holdings; the contract controls/extends operations. Index the balance exactly once under the ASA ID, and attach the controlling application ID and verified ARC-20 classification as metadata. See the [ARC-20 Smart ASA specification](https://dev.algorand.co/arc-standards/arc-0020/).

ARC-200 is a separate smart-contract token interface, keyed by application ID. Index its Transfer events from application logs, including inner execution, and retain decoder/support status. Unlike ARC-20, do not represent ARC-200 as an ASA holding. See the [ARC-200 specification](https://dev.algorand.co/arc-standards/arc-0200/).

| Token | Canonical identity | Balance source |
|---|---|---|
| ALGO | `kind=algo, token_id=0` | Payment, fee, reward/payout, and close effects from canonical block execution |
| ASA | `kind=asa, token_id=asset_id` | ASA transfers, close amounts, opt-in/out, asset lifecycle |
| ARC-20 | Same `kind=asa, token_id=asset_id` plus contract metadata | ASA ledger effects; controlling app is enrichment, not another balance stream |
| ARC-200 | `kind=arc200, token_id=application_id` | Decoded Transfer events from application execution |

All amounts are exact integers. Store uint64-sized values in a representation that does not overflow signed 64-bit integers (for example, canonical decimal strings in Cassandra `text`, with checked `BigInteger` arithmetic in .NET). Do not use floating point. Addresses can be stored as 32-byte `blob` keys and rendered as Algorand addresses at the API boundary.

## Cassandra reference implementation

Cassandra can work well when write throughput, horizontal scale, and availability are primary goals, but it is not a drop-in relational replacement. Its model is query-first: define one table per access pattern, denormalize repeated fields, and always query by partition key. It does not provide joins or arbitrary historical aggregation like SQL. This follows [Apache Cassandra’s data-modeling guidance](https://cassandra.apache.org/doc/stable/cassandra/developing/data-modeling/intro.html).

This design stores no periodic balance or holder snapshots. It keeps immutable balance effects, current balances, and a token-to-ever-touched-accounts index. Historical single-account balances are calculated from that account’s effects. Historical holder lists are computed by streaming and aggregating token effects, or by checking each touched account; both are slower than snapshots, especially for high-activity tokens. This avoids snapshot storage at the cost of read I/O and computation.

### Logical identifiers

Use a chain-scoped namespace on every row or keep each chain in its own keyspace/database. Suggested token key: `(network, token_kind, token_id)`. Store all token metadata duplicated where needed in read tables; do not expect joins at request time.

### Tables by API query

The following CQL is a logical starting point; sizes, bucketing, TTL, and clustering keys must be load-tested and tuned. Use bounded time/round buckets so no partition grows indefinitely. `shard` is a stable hash bucket, not a random value per write.

```sql
-- API: latest chain round, and restart/replay cursor
CREATE TABLE chain_status (
  chain text PRIMARY KEY,
  indexed_round bigint,
  indexed_block_hash blob,
  target_round bigint,
  status text,
  updated_at timestamp
);

-- Token metadata lookup; copied into other query tables only when useful
CREATE TABLE token_by_id (
  token_kind text,
  token_id bigint,
  creator blob,
  decimals smallint,
  symbol text,
  name text,
  standard text,                 -- ASA / ARC-20 / ARC-200 / unknown
  standard_verified boolean,
  controlling_app_id bigint,
  created_round bigint,
  deleted_round bigint,
  metadata_json text,
  PRIMARY KEY ((token_kind, token_id))
);

-- API: current assets/balances for one account; one row per holding
CREATE TABLE holdings_by_account (
  account blob,
  token_kind text,
  token_id bigint,
  amount text,
  opted_in boolean,
  last_changed_round bigint,
  symbol text,
  decimals smallint,
  standard text,
  PRIMARY KEY ((account), token_kind, token_id)
);

-- API: transaction effects for one account + token, in a bounded month/round bucket
CREATE TABLE effects_by_account_token_bucket (
  account blob,
  token_kind text,
  token_id bigint,
  bucket int,
  round bigint,
  intra_round_order int,
  transaction_id text,
  inner_path list<int>,
  effect_type text,
  delta text,
  balance_after text,
  counterparty blob,
  PRIMARY KEY ((account, token_kind, token_id, bucket), round, intra_round_order, transaction_id)
) WITH CLUSTERING ORDER BY (round DESC, intra_round_order DESC, transaction_id ASC);

-- Directory of buckets containing effects for this account/token, for round lookup.
CREATE TABLE effect_buckets_by_account_token (
  account blob,
  token_kind text,
  token_id bigint,
  bucket int,
  first_round bigint,
  last_round bigint,
  PRIMARY KEY ((account, token_kind, token_id), bucket)
) WITH CLUSTERING ORDER BY (bucket DESC);

-- API: history from the asset/holder side or backfill/audit; duplicate effect rows
CREATE TABLE effects_by_token_bucket (
  token_kind text,
  token_id bigint,
  bucket int,
  shard smallint,
  round bigint,
  intra_round_order int,
  account blob,
  transaction_id text,
  effect_type text,
  delta text,
  PRIMARY KEY ((token_kind, token_id, bucket, shard), round, intra_round_order, account, transaction_id)
) WITH CLUSTERING ORDER BY (round DESC, intra_round_order DESC);

-- Enumerate every account that has ever touched a token, including zero balances.
-- Shard this directory so a popular token does not create one giant partition.
CREATE TABLE accounts_touched_by_token (
  token_kind text,
  token_id bigint,
  shard smallint,
  account blob,
  first_round bigint,
  last_round bigint,
  PRIMARY KEY ((token_kind, token_id, shard), account)
);
```

### How reads work without snapshots

- **Current account holdings:** read `holdings_by_account` by account. Positive balances are holders; `opted_in=true` and amount zero can be included when requested.
- **Account/token history:** read that account/token’s effect rows from the relevant bounded round buckets. This is a direct partition-key lookup.
- **Account/token balance at round R:** use `effect_buckets_by_account_token` to locate the latest non-empty bucket at or before R, then read the newest effect at or before R from `effects_by_account_token_bucket`; return its `balance_after`. If that bucket has no event at or before R, try the previous non-empty bucket. This is a latest-row lookup rather than summing all prior effects. `balance_after` is computed by the ordered indexer and stored on every effect row; it is per-event state, not a periodic snapshot. If minimum storage matters more than balance-query latency, omit it and use the slower cumulative-delta scan.
- **All holders at round R, recommended no-snapshot path:** stream `effects_by_token_bucket` for this token across its shards and buckets through R, then aggregate deltas by account with bounded-memory aggregation (spill to temporary disk when needed). Return accounts whose balance is positive; optionally include zero-balance accounts by merging the touched-account directory. Read cost is proportional to all effects for that token through R.
- **Alternative holder path:** page `accounts_touched_by_token`, then read each account’s history through R and sum. This causes N account-history reads and can be much slower than one streaming token-history pass. Use bounded concurrency; never issue unbounded N+1 fan-out.

Without holder snapshots, a popular token may require reading millions of effects, even for an old round. Single-account historical balance queries avoid that scan when `balance_after` is stored per effect; all-holder historical queries do not. Support asynchronous jobs for large holder scans, expose progress, and apply limits/timeouts for synchronous queries. If arbitrary historical holder queries later need consistently low latency, that conflicts with the no-snapshot constraint and requires a deliberate change in storage/latency requirements.

Never use unbounded `ALLOW FILTERING` or global scans. Token effects must be addressable by token, bucket, and shard. Use deterministic sharding and fixed-size round/time buckets so partitions stay bounded. Keep zero-balance accounts in `accounts_touched_by_token`; do not delete them if past-round holder queries must include prior holdings.

## PostgreSQL reference implementation

Use PostgreSQL-specific SQL and indexes for the same API contract. For hot point reads, keep account effects ordered by account/token/round and store `balance_after` on every effect. An account-hash-partitioned table lets PostgreSQL prune to one of a fixed number of partitions for the balance lookup. A separate token-oriented effects table supports holder-history scans. The duplicated token-oriented view costs disk but gives a direct sequential scan path, matching the Cassandra query-table strategy.

```sql
CREATE TABLE chain_status (
  chain text PRIMARY KEY,
  indexed_round bigint NOT NULL,
  indexed_block_hash bytea NOT NULL,
  target_round bigint,
  status text NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE token (
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  creator bytea,
  decimals smallint,
  symbol text,
  name text,
  standard text,
  standard_verified boolean NOT NULL DEFAULT false,
  controlling_app_id bigint,
  created_round bigint,
  deleted_round bigint,
  metadata_json jsonb,
  PRIMARY KEY (token_kind, token_id)
);

CREATE TABLE holdings_by_account (
  account bytea NOT NULL,
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  amount numeric(20,0) NOT NULL,
  opted_in boolean NOT NULL,
  last_changed_round bigint NOT NULL,
  PRIMARY KEY (account, token_kind, token_id)
);

-- Partition by account hash (fixed partition count chosen at deployment).
CREATE TABLE effects_by_account_token (
  account bytea NOT NULL,
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  round bigint NOT NULL,
  intra_round_order integer NOT NULL,
  transaction_id text NOT NULL,
  inner_path integer[] NOT NULL DEFAULT '{}',
  effect_type text NOT NULL,
  delta numeric(20,0) NOT NULL,
  balance_after numeric(20,0) NOT NULL,
  counterparty bytea,
  PRIMARY KEY (account, token_kind, token_id, round, intra_round_order, transaction_id)
) PARTITION BY HASH (account);

CREATE INDEX effects_account_token_round_idx
  ON effects_by_account_token
  (account, token_kind, token_id, round DESC, intra_round_order DESC)
  INCLUDE (balance_after, delta, transaction_id);

-- Duplicated query view partitioned by bounded round bucket for token scans.
CREATE TABLE effects_by_token_bucket (
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  bucket integer NOT NULL,
  round bigint NOT NULL,
  intra_round_order integer NOT NULL,
  account bytea NOT NULL,
  transaction_id text NOT NULL,
  effect_type text NOT NULL,
  delta numeric(20,0) NOT NULL,
  PRIMARY KEY (token_kind, token_id, bucket, round, intra_round_order, account, transaction_id)
) PARTITION BY RANGE (bucket);

CREATE INDEX effects_token_bucket_round_idx
  ON effects_by_token_bucket (token_kind, token_id, bucket, round, intra_round_order);

CREATE TABLE accounts_touched_by_token (
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  account bytea NOT NULL,
  first_round bigint NOT NULL,
  last_round bigint NOT NULL,
  PRIMARY KEY (token_kind, token_id, account)
);
```

Create a fixed set of hash partitions for `effects_by_account_token` and bounded range partitions for `effects_by_token_bucket` ahead of ingestion. The balance query uses equality on account/token plus `round <= R`, ordered newest-first with limit one; hash partition pruning routes it to one physical partition. The holder query streams the token-oriented partitions through R and aggregates effects; no snapshots are introduced. PostgreSQL uses `numeric(20,0)` for uint64-safe exact amounts while Cassandra uses decimal text plus checked .NET `BigInteger` arithmetic.

PostgreSQL applies each round’s effects and advances `chain_status` in a transaction, so the publication cursor and round rows can commit atomically. Cassandra uses the idempotent multi-partition write and final publication workflow described below. Both must return identical API results and support deterministic replay.

### Idempotency, consistency, and round commits

Cassandra does not provide a relational transaction spanning all partitions. Do not rely on a multi-partition batch as an atomic round commit. Use deterministic primary keys and idempotent upserts for all effects, and maintain a round-level workflow:

1. Persist a round as `processing` in a durable ingestion journal (Cassandra table or external queue/object store).
2. Derive all canonical balance effects with stable keys from chain, round, transaction ID, inner path, and effect ordinal; compute each account/token’s ordered `balance_after` during round processing.
3. Upsert query tables idempotently, using explicit consistency levels and retry policy.
4. Verify expected write counts/checksums and then advance the visible `chain_status.indexed_round` as the final publication step.
5. On restart, replay incomplete rounds safely; detect unexpected block-hash changes and rebuild by replaying from the last verified round or archived block.

This makes API reads expose only completed rounds while allowing independently written query tables to converge. Keep an immutable raw/canonical block archive (object storage is suitable) or another replayable source so the database can be rebuilt. Avoid Cassandra counters for balances; compute exact totals from deterministic effects and write absolute current balance values.

## Kubernetes deployment model

Deploy all application and database components to Kubernetes. Use one isolated deployment/database per chain environment:

| Deployment | Chain | Database |
|---|---|---|
| Stage | Algorand TestNet | Dedicated TestNet PostgreSQL database or Cassandra keyspace/cluster |
| Production | Algorand MainNet | Dedicated MainNet PostgreSQL database or Cassandra keyspace/cluster |
| Production | Voi | Dedicated Voi PostgreSQL database or Cassandra keyspace/cluster |

Choose the provider independently for each chain deployment using `Storage:Provider`. For failure and resource isolation, run separate PostgreSQL or Cassandra clusters per production chain; at minimum use separate databases/keyspaces, credentials, secrets, network policies, PVCs, and resource budgets. Keyspaces alone are not a hard isolation boundary. Stage must not share production credentials or persistent volumes.

Kubernetes components per chain:

- Cassandra backend: cluster managed by a Kubernetes-aware Cassandra operator, with StatefulSets, persistent volumes, pod anti-affinity, disruption budgets, topology-aware placement, and tested backup/restore. Do not deploy Cassandra as an ordinary stateless Deployment.
- PostgreSQL backend: HA cluster managed by a Kubernetes-aware operator, with persistent volumes, replication, backup/PITR, connection pooling, and tested restore. Do not use an ephemeral database pod.
- ASP.NET Core API Deployment, horizontally scalable, with readiness/liveness probes and resource requests/limits.
- .NET indexer worker Deployment with a single active ordered writer per chain; use leader election/lease if multiple replicas are configured for failover.
- Optional async historical-query workers for large holder scans, with bounded shard concurrency.
- Algorand-compatible archival node/Conduit or supported block source as Kubernetes workloads, configured per chain. Verify Voi’s endpoint/consensus compatibility separately; do not assume an Algorand MainNet endpoint serves Voi history.
- ConfigMaps for non-secret settings, Secrets for node/database credentials, NetworkPolicies, TLS, service accounts/RBAC, and PodDisruptionBudgets.
- Persistent volume classes sized and benchmarked for the selected database, with topology-aware storage and replicas across failure domains.
- Metrics, logs, and alerts for indexed round, chain lag, round failures, Cassandra compaction/repair or PostgreSQL vacuum/replication status, disk, request latency, historical scan duration, and decoder errors.

Keep separate Helm values or Kustomize overlays for `stage-testnet`, `prod-algorand`, and `prod-voi`. Use the same versioned container images and migrations across them, with chain-specific configuration. A Kubernetes namespace separates release/configuration scope; storage, credentials, and network policy must also be isolated.

## .NET implementation plan

1. **Lock down access patterns and SLOs.** Set bounds for history range, holder response/page size, expected round lag, historical-query latency, and whether arbitrary-round holders must be fast or may be slower. Decide whether exact end-of-round balance includes all protocol rewards/fees.
2. **Implement both persistence providers.** Build PostgreSQL DDL/index/partition management and Cassandra CQL tables for the same logical effects and API queries. Cover ALGO, ASA, ARC-20, ARC-200, opt-in/out, close, inner transactions, zero balances, historical balances, holders, and transaction history.
3. **Build shared .NET domain/indexing library.** Normalize each network’s block/transaction data into deterministic token effects. Keep chain-specific adapters for genesis/network parameters and source APIs.
4. **Implement replayable ingestion.** Read from an archival source/Conduit-compatible stream. Process rounds sequentially per chain; parallelize decoding, but publish commits in round order. Use exact arithmetic and provider-specific idempotent writes. PostgreSQL commits round data and cursor transactionally; Cassandra uses the publication workflow described above.
5. **Bootstrap and validate.** Backfill TestNet first, then production chain deployments. Expose backfill progress. Compare sampled balances, holders, and transaction effects against a trusted Indexer/source at the same rounds. Validate Voi separately.
6. **Add incremental workers.** Resume from stored round/hash cursor, process new rounds, retry safely, and alert on lag and gaps. Publish a round only when all required tables are complete.
7. **Build API read models.** ASP.NET Core endpoints read precomputed query tables; avoid request-time table scans or broad aggregation. Use stable cursors, bounded ranges, per-chain status, and exact amount strings in JSON for JavaScript clients.
8. **Implement no-snapshot historical queries.** Stream effects by token bucket/shard and aggregate with bounded memory or temporary disk; provide async jobs for queries exceeding synchronous budgets. Benchmark worst-case holder scans.
9. **Deploy on Kubernetes.** Package API, indexer, historical-query workers, and chain data-source components as containers. Provision the configured PostgreSQL or Cassandra backend and separate credentials/storage for TestNet, MainNet, and Voi. Add GitOps/Helm deployment, probes, autoscaling where safe, backups, restore drills, and rolling upgrade procedures.
10. **Benchmark and select.** Replay the same canonical dataset into each backend. Measure ingest throughput, database size including indexes/replication, current and historical balance p50/p95/p99 under parallel load, account history, current holders, historical holders, recovery/replay time, and operational effort. Publish results and select `Storage:Provider` independently in each environment.
11. **Operate and tune.** Monitor provider-specific health: Cassandra compaction/repair/tombstones/hot partitions, or PostgreSQL vacuum/replication/index bloat/partition growth, plus disk headroom. Scale API replicas independently and rehearse database scale/restore procedures.

Suggested .NET pieces: ASP.NET Core; Npgsql and Dapper for PostgreSQL; the DataStax C# driver for Cassandra; provider-specific schema migration/bootstrap tools; background services for round and historical-query work; OpenTelemetry; structured logs. Use a shared repository interface with two concrete adapters. EF Core may be used for PostgreSQL metadata if useful, but not as the cross-provider abstraction for Cassandra.

For a heavy balance-read workload, benchmark this per-event `balance_after` model on Apache Cassandra and ScyllaDB using realistic account/token skew, request concurrency, and data volume. ScyllaDB speaks Cassandra-compatible CQL, but it is not automatically faster for every workload. PostgreSQL is also credible for this point lookup: a B-tree ordered by account, token, round, and intra-round order can return the latest event at or before a round with `ORDER BY ... DESC LIMIT 1`. PostgreSQL may be simpler at moderate scale; Cassandra or ScyllaDB may fit better when horizontal read/write scaling and multi-node availability are requirements. Choose using measured p95/p99 latency and throughput rather than a blanket claim that one database is faster.

## API outline

Use `/v1/{chain}` so callers always identify `algorand-testnet`, `algorand-mainnet`, or `voi` explicitly.

- `GET /v1/{chain}/accounts/{address}/tokens?includeZero=false&limit=...`
- `GET /v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/balance?round=R`
- `GET /v1/{chain}/tokens/{kind}/{tokenId}/holders?round=R&includeZero=false&limit=...`
- `GET /v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/transactions?fromRound=...&toRound=...&limit=...`
- `GET /v1/{chain}/tokens/{kind}/{tokenId}`
- `GET /v1/{chain}/status` — indexed round, target round, lag, historical-query mode, and ingestion health.

Return `indexedRound` with query responses. Reject future rounds explicitly. Document that holders are a paginated end-of-round result computed by aggregating token effects through the requested round.

## Main risks / decisions

- **Historical holder query cost:** without snapshots, historical holder queries must scan and aggregate all relevant token effects or fan out across all touched accounts. Benchmark high-activity assets and define latency/async-query limits. Per-account historical balance can remain fast by storing the balance after every effect.
- **Cross-table consistency:** a round touches many partitions and query tables. Use idempotent writes plus publication cursor; do not expose a round until all required writes have completed.
- **All accounts definition:** clarify whether “all accounts” means addresses ever seen, current ALGO accounts, or accounts with any token opt-in. The proposed API covers addresses seen in indexed chain activity and preserves zero opted-in holdings.
- **Chain adapters:** Algorand MainNet, TestNet, and Voi need separate cursors, genesis/network configuration, data sources, and validation. Voi-specific compatibility and historical access must be confirmed in a prototype.
- **ARC-200 support:** standard events are the primary source, but contract implementations can vary. Preserve decoder identity and verification status.
- **Kubernetes operations:** Cassandra needs persistent storage, careful node topology, repair, compaction, backup, and restore procedures. Stateful workload operations are part of the product, not an afterthought.

## Sources

- [Apache Cassandra data modeling](https://cassandra.apache.org/doc/stable/cassandra/developing/data-modeling/intro.html)
- [Apache Cassandra logical data modeling](https://cassandra.apache.org/doc/latest/cassandra/developing/data-modeling/data-modeling_logical.html)
- [Apache Cassandra CQL and partition keys](https://cassandra.apache.org/doc/latest/cassandra/developing/cql/ddl.html)
- [Algorand Conduit installation](https://dev.algorand.co/nodes/installation/conduit-installation/)
- [Algorand archival node settings](https://dev.algorand.co/nodes/reference/config-settings/)
- [Voi API documentation](https://docs.voi.network/developers/api/)
- [Voi block overview](https://docs.voi.network/understanding-voi/blocks/)
- [ARC-20 Smart ASA](https://dev.algorand.co/arc-standards/arc-0020/)
- [ARC-200](https://dev.algorand.co/arc-standards/arc-0200/)

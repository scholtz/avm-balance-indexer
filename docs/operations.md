# Operations runbook

For people who run an instance. Design background is in [docs/adr](adr); the settings are listed in the README
([Configuration](../README.md#configuration)) and the Helm values ([deploy/README.md](../deploy/README.md)).

## What runs

One **deployment per chain** (`algorand-mainnet`, `algorand-testnet`, `voi`) with its own database or keyspace and credentials:

| Component | Replicas | Role |
|---|---|---|
| API (`avm-api`) | 2 or more, stateless | Serves `/v1/{chain}`, `/healthz/*`, `/openapi/v1.json`. Reads the database only. |
| Worker (`avm-worker`) | 2 by default | One replica holds the **writer lease** and indexes; the others stand by. Every replica also runs scan jobs and samples the chain status for metrics. |
| Database | PostgreSQL or Cassandra | Chosen per deployment with `Storage:Provider`. Never dual-written. |

Probes: API `/healthz/live` and `/healthz/ready` (ready needs the database); worker `/healthz/live` and `/healthz/ready` on `Worker:HealthPort`
(ready means the replica knows whether it leads or stands by, a standby is ready).

## Sizing and layout decisions that are fixed per database

These cannot change without indexing into a fresh database or keyspace:

| Setting | Provider | Meaning |
|---|---|---|
| `Storage:PostgreSql:AccountHashPartitions` | PostgreSQL | Hash partitions of the account effect table (1 to 1024, default 64). The worker refuses to start when it differs from the existing database. |
| Round bucket size (100,000 rounds) | both | Bounds partition growth; compiled in (`RoundBuckets.Size`). |
| Token shards (16) | Cassandra | Spreads token-oriented tables; compiled in. |
| `Storage:Cassandra:Keyspace`, `LocalDatacenter`, `ReplicationFactor` | Cassandra | Keyspace name, datacenter the driver routes to, replicas per datacenter (default 3). Use 1 only on a single-node development cluster; reads and writes need a `LOCAL_QUORUM`. |

Notes that matter for capacity:

- The fee sink and the rewards pool receive an effect in nearly every round. They are hot partitions on both providers (ADR 0002 and 0003); measure
  before MainNet and prefer your own archival nodes (`Algorand:GossipHosts`) for backfill.
- A past round of a very large token is answered account by account. Holders at the **latest** round are one index scan. For exports of a large token at a past round use
  asynchronous scans (`POST /v1/{chain}/tokens/{kind}/{tokenId}/holders/scans`, ADR 0012).
- Backfill speed depends on the archival source and `Indexer:FetchConcurrency`. The worker keeps `Indexer:BalanceCacheEntries` account balances in memory (default
  500,000 entries): size the worker memory limit accordingly.

## Deploy

1. Create the database (operator-managed) and a Secret with its credentials; the chart only consumes existing Secrets.
2. Pick the overlay (`deploy/helm/values/*.yaml`) and set `storage.provider`, the connection settings, `algorand.algodUrl` and `algorand.gossipHosts`.
3. `helm upgrade --install <release> deploy/helm/avm-balance-indexer -n <namespace> -f <overlay>`.
4. The first worker applies the schema (PostgreSQL scripts, or Cassandra keyspace and tables when `Storage:Cassandra:CreateKeyspace` is true), writes the genesis allocation
   and starts backfilling. `GET /v1/{chain}/status` shows `state`, `indexedRound`, `targetRound` and `lag`.
5. Behind an ingress set `Api:TrustForwardedHeaders` (with `Api:KnownProxyNetworks` and `Api:ForwardedHeadersLimit` when the API is reachable in other ways or through
   several hops), otherwise every client shares the rate-limit bucket of the ingress address.

Security: put the database credentials in Secrets, enable `Storage:Cassandra:UseTls` when credentials are used, keep the network policy that the chart creates, and do not
expose the worker health port outside the cluster.

## Upgrade

Rules that every release has to respect (they come from the ADRs):

1. **Upgrade the worker first.** Schema changes are applied by the worker when it starts (ADR 0005 migrations, ADR 0012 scan tables). The API must not be upgraded before a new
   worker has applied the schema: newer queries select new columns.
2. **No writer of the previous version may run while a migration is applied.** The chart rolls workers with `maxUnavailable: 0`, so an old replica can hold the writer lease while
   a new one applies the schema. For a release that contains a migration, scale the worker to one replica, upgrade it, then scale back up.
3. **Replaying is the only way to change layout settings** (partitions, buckets, shards) and to fix history after a normalizer change (ADR 0003): index into a fresh database or
   keyspace, compare, then switch the release to it.
4. Never edit `holdings_by_account` or `holdings_by_token` by hand on Cassandra: the rows carry the round as their timestamp and a manual write would win over every later round.

## Backup, restore and reindex

- **PostgreSQL:** regular base backups plus WAL archiving (point-in-time recovery) through the operator. A restore is consistent because every round commits atomically; the worker
  resumes from the restored cursor and re-indexes the missing rounds from the archive.
- **Cassandra:** snapshots of the keyspace on every node (operator-managed) and, for a full restore, the same snapshot on all nodes. A cursor ahead of the restored data is not
  possible because publication is the last step of a round; rows beyond the cursor are overwritten when the round is indexed again.
- **Reindex:** the data is derived from the chain, so the safest "restore" is a fresh deployment indexing from round 0. Run it next to the live one with its own database and switch
  the release when the lag is 0 and the live comparison tests (`dotnet test tests/AvmBalanceIndexer.Live.Tests`) pass against the chain.
- Scan results are temporary (`Scans:Retention`) and are not worth backing up.

## Monitoring and alerts

Set `telemetry.otlpEndpoint` (ADR 0011). The metrics:

| Metric | Alert |
|---|---|
| `avm.indexer.lag_rounds` | Warn when it stays above a few hundred rounds at the tip; critical when it only grows. |
| `avm.indexer.status_age_seconds` | Critical above a few minutes: the writer has not updated the status (stuck, no leader, database away). |
| `avm.indexer.indexed_round` | Should rise by about one round per few seconds on MainNet; flat means stuck. |
| `avm.indexer.status_sample_failures` | Rises while a replica cannot read the database; the other `avm.indexer.*` gauges are then stale. |
| `avm.worker.is_leader` | The sum over replicas must be 1; 0 for longer than the lease time is critical, 2 is a bug. |
| `avm.worker.leases_lost` | Alert on a rate above a few per hour (flapping connection or Cassandra timeouts). |
| `avm.worker.leases_acquired` | A step of 1 is a failover; many in a short time is flapping. |
| `avm.worker.transient_failures` | Sustained growth means the archive, algod or the database is unhealthy. |
| `avm.worker.rounds_committed` | Throughput of this replica; zero on the leader while lag is large means stuck. |
| ASP.NET Core `http.server.request.duration`, Kestrel and runtime metrics | Latency and error-rate SLOs of the API; 429 shows the rate limiter at work. |

Also watch the database itself: disk, replication, connections, vacuum and bloat (PostgreSQL), compaction, repair, tombstones and hot partitions (Cassandra).

## Failure modes

| Symptom | Cause | What to do |
|---|---|---|
| Worker exits with a *chain divergence* | A block does not link to the stored cursor (archive served another chain, or the database holds a different history). Indexing stopped on purpose, nothing is written on top (ADR 0002). | Check `Algorand:GossipHosts` and `Algorand:ExpectedGenesisHash` and the node. If the database is wrong, reindex into a fresh one. |
| Worker exits after many transient failures | `Indexer:MaxConsecutiveFailures` consecutive failures without progress (archive or algod unreachable, or a block the archive never serves). | Fix the source; the orchestrator restarts the pod and indexing resumes from the cursor. |
| `avm.worker.is_leader` is 0 on all replicas | Database unreachable, or a lease that has not expired yet. PostgreSQL releases the lease when the dead session is dropped (about 30 s with the lease session keepalives); Cassandra after `Storage:Cassandra:LeaseTtlSeconds`. | Wait one TTL; check database reachability. |
| Repeated lease losses | Cassandra timeouts or an unstable PostgreSQL connection. | Raise `Storage:Cassandra:LeaseTtlSeconds` (a lease renewal must fit well inside it) or fix the network; check `ReadTimeoutSeconds` is not set below what the cluster can do. |
| `503` "took too long" on holders | A holders-at-round request exceeded `ReadTimeoutSeconds`. | Query the latest round, a smaller page, or use an asynchronous scan. |
| `503` "chain has not been indexed yet" | The worker has not published the first round (new deployment or empty database). | Wait for the genesis allocation; check the worker log. |
| `429` | Rate limit per client IP. | Raise `Api:RateLimitPerMinute` or fix the client; behind an ingress make sure forwarded headers are trusted so clients are not merged. |
| Scan stays `Pending` | No worker replica runs, or the scan tables do not exist yet (the worker creates them). | Check the worker; scans of a replica that died are taken over after `Scans:StaleAfter`. |
| Scan `Failed` | A step kept timing out, or an unexpected error (see the worker log for the scan id). | Retry later or ask for a smaller token or the latest round. |
| API `500` mentioning a missing column | The API was upgraded before the worker applied the new schema. | Upgrade the worker (see the upgrade rules), the API recovers without restart. |

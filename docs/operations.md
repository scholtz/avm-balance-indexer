# Operations runbook

For people who run an instance. Design background is in [docs/adr](adr); the settings are listed in the README
([Configuration](../README.md#configuration)) and the Helm values ([deploy/README.md](../deploy/README.md)).

## What runs

One **deployment per chain** (`algorand-mainnet`, `algorand-testnet`, `voi`) with its own database or keyspace and credentials:

| Component | Replicas | Role |
|---|---|---|
| API (`avm-api`) | 2 or more, stateless | Serves `/v1/{chain}`, `/healthz/*`, `/openapi/v1.json` and the Swagger UI at `/swagger` (`/` redirects there). Reads the database only. |
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
| `Storage:Cassandra:Keyspace`, `Storage:Cassandra:LocalDatacenter`, `Storage:Cassandra:ReplicationFactor` | Cassandra | Keyspace name, datacenter the driver routes to, replicas per datacenter (default 3). Use 1 only on a single-node development cluster; reads and writes need a `LOCAL_QUORUM`. |

Notes that matter for capacity:

- The fee sink and the rewards pool receive an effect in nearly every round. They are hot partitions on both providers (ADR 0002 and 0003); measure
  before MainNet and prefer your own archival nodes (`Algorand:GossipHosts`) for backfill.
- A past round of a very large token is answered account by account. Holders at the **latest** round are one index scan. For exports of a large token at a past round use
  asynchronous scans (`POST /v1/{chain}/tokens/{kind}/{tokenId}/holders/scans`, ADR 0012).
- Backfill speed depends on the archival source and `Indexer:FetchConcurrency`. Public archival nodes answer `429` when they are asked too much: the fetcher then slows that node down on its own (per-node concurrency limit, `Retry-After`, see `Algorand:MaxConcurrentRequestsPerHost` in the README), so the `Archival nodes are rate limiting` summary (about one per `Algorand:ThrottleLogInterval`, a minute) is not an error; a fetch only fails after `Algorand:ThrottleTimeout` of continuous rate limiting, which means the nodes cannot carry the load: lower `Indexer:FetchConcurrency` or use your own nodes. The worker keeps `Indexer:BalanceCacheEntries` account balances in memory (default
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

The GitHub deploy workflows (docs/deployment-pipelines.md, ADR 0013) perform both rules below themselves; the commands are for manual upgrades.

Rules that every release has to respect (they come from the ADRs):

1. **Upgrade the worker first.** Every worker replica applies pending schema changes when it starts (ADR 0005 migrations, ADR 0012 scan tables). The API must not be upgraded
   before a new worker has applied the schema: newer queries select new columns. Do it in two steps with the per-component image tags:
   `helm upgrade ... --set image.apiTag=<old> --set image.workerTag=<new>`, wait until the new workers are ready (`kubectl rollout status`), then
   `helm upgrade ... --set image.tag=<new>` (without the two per-component tags). Migrations are additive (new tables and columns that the previous API simply
   does not use), which is what keeps the old API working in between; a release with a breaking migration says so in its notes and needs the API scaled to 0 for the
   duration.
2. **No writer of the previous version may run while a migration is applied.** With the default `worker.strategy: RollingUpdate` an old replica can keep the writer lease while a
   new pod applies the schema, whatever the replica count. For a release that contains a migration (the release notes say so) set `worker.strategy=Recreate`, or scale the worker to 0 before
   the upgrade and back afterwards, so that the old writer is gone first.
3. **Replaying is the only way to change layout settings** (partitions, buckets, shards) and to fix history after a normalizer change (ADR 0003): index into a fresh database or
   keyspace, compare, then switch the release to it.
4. Never edit `holdings_by_account` or `holdings_by_token` by hand on Cassandra: the rows carry the round as their timestamp and a manual write would win over every later round.

## Backup, restore and reindex

- **PostgreSQL:** regular base backups plus WAL archiving (point-in-time recovery) through the operator. A restore is consistent because every round commits atomically; the worker
  resumes from the restored cursor and re-indexes the missing rounds from the archive.
- **Cassandra:** snapshots of the keyspace on every node (operator-managed). Snapshots of different nodes are taken at slightly different moments, so a restored cluster can
  hold a cursor that is ahead of effect rows on a node restored from an earlier moment. After a restore run a repair, then compare balances with the chain (below); when in doubt reindex.
  Within a live cluster publication is the last step of a round and rows beyond the cursor are overwritten when the round is indexed again.
- **Reindex:** the data is derived from the chain, so the safest "restore" is a fresh deployment indexing from round 0. Run it next to the live one with its own database and switch
  when its lag is 0 and it agrees with the live one: pick a round R that both have published and compare `GET /v1/{chain}/tokens/algo/0/holders?round=R` (sum the amounts, count the
  holders) and a sample of `.../accounts/{address}/tokens/algo/0/balance?round=R`. The first 1000 rounds of the real chains are proven against the ledger by
  `dotnet test tests/AvmBalanceIndexer.Live.Tests`; that test does not read a deployment.
- Scan results are temporary (`Scans:Retention`) and are not worth backing up.

### Reindex after a normalizer fix (runbook)

A fix in the normalizer or the tracker only changes rounds that are indexed afterwards. Rows already written stay as they were, so a stalled or wrong deployment
keeps stalling until it is rebuilt (issue #115 was fixed in code, yet the TestNet worker kept failing on the old rows). Cassandra rows must not be edited (ADR 0003), so
rebuild into a new keyspace.

Choose how to switch:

- **The deployment is stalled or serves wrong data** (TestNet after #115): change the keyspace in the release values. The API switches to the rebuilt data at once and reports a low
  `indexedRound` until it catches up (every response carries `indexedRound`, so clients can tell, but balances and holders are incomplete until then); the old keyspace stays untouched, so restoring
  the old value switches back with another rollout, as long as the old keyspace has not been dropped.
- **The deployment is healthy and must keep serving** (MainNet): run a second release with its own keyspace next to it, compare, and switch the first release only when the rebuilt
  one has lag 0 and agrees with the chain.

Steps:

1. Set `storage.cassandra.keyspace` in `deploy/helm/values/<env>-<chain>.yaml` to a new name (`avm_testnet_2`, then `_3`, ...) and merge it. The worker creates the keyspace
   (`createKeyspace: true`; only the worker applies the schema) and indexes from genesis. Storage queries fail while the keyspace does not exist, so the readiness check of a new API pod fails and Kubernetes keeps the old pod until the worker has
   created it. The worker of the old keyspace is replaced by the rollout; when it was still indexing, set `worker.strategy=Recreate` for this release so that two writers never download side by side.
2. Watch `GET /v1/{chain}/status`: `state` is `Backfilling`, `indexedRound` rises, and the worker pod does not restart (`kubectl -n avm-balance-indexer logs deploy/<release>-<chain>-worker`).
   The worker log line `Indexing of <chain> stopped after round <R> ...` names the cause if it stops.
3. When `state` is `Following`, compare with the chain: pick accounts that received asset close-outs and compare
   `GET /v1/{chain}/accounts/{address}/tokens/asa/{id}/balance?round=R` with the holding from the Nodely indexer (`GET https://testnet-idx.4160.nodely.dev/v2/accounts/{address}/assets/{id}`, or `mainnet-idx` for MainNet,
   for the current round), plus the checks of the reindex bullet above.
4. Reclaim the space with `DROP KEYSPACE <old keyspace>;` through `cqlsh` on the Cassandra pod once nobody needs the old data. The drop cannot be undone and ends the rollback option of step 1
   (the old name would be recreated empty). Both keyspaces share the node's disk (`cassandra.embedded.hostPath`) while they coexist: check the free space before a rebuild of a large chain.

Which deployments need it after #115: every database that indexed an asset close-out (`aclose`) of protocols v0 to v24. On TestNet the range that was indexed (rounds 0 to 4,982,988)
contains such close-outs. MainNet ran the same early protocols and is affected as soon as its indexed range holds one asset close-out with an amount: list them with the Nodely indexer
(`GET https://mainnet-idx.4160.nodely.dev/v2/transactions?tx-type=axfer&min-round=A&max-round=B&limit=1000`, keep `asset-transfer-transaction.close-to` set and `close-amount` above 0; a block of a protocol
before v25 has no `aca`). Each such transaction proves the missing credit for its `close-to` account, and that account is the one to compare in step 3. Voi launched on a recent protocol and
is expected to be unaffected; this was not verified.

## Monitoring and alerts

Set `telemetry.otlpEndpoint` (ADR 0011). The metrics:

| Metric | Alert |
|---|---|
| `avm.indexer.lag_rounds` | Warn when it stays above a few hundred rounds at the tip; critical when it only grows. |
| `avm.indexer.status_age_seconds` | Critical above a few minutes: the writer has not updated the status (stuck, no leader, database away). |
| `avm.indexer.indexed_round` | Should rise by about one round per few seconds on MainNet; flat means stuck. |
| `avm.indexer.status_sample_failures` | Rises while a replica cannot read the database; the other `avm.indexer.*` gauges are then stale. |
| `avm.worker.is_leader` | The sum over live replicas should be 1. Alert when it is 0 for longer than the lease time plus a minute. A sum of 2 can appear for a moment while an old pod's last sample is still visible: alert only when it lasts several minutes (`for: 5m`). |
| `avm.worker.leases_lost` | Alert on a rate above a few per hour (flapping connection or Cassandra timeouts). |
| `avm.worker.leases_acquired` | A step of 1 is a failover; many in a short time is flapping. |
| `avm.worker.transient_failures` | Sustained growth means the archive, algod or the database is unhealthy. |
| `avm.worker.rounds_committed` | Throughput of this replica; zero on the leader while lag is large means stuck. |
| ASP.NET Core `http.server.request.duration`, Kestrel and runtime metrics | Latency and error-rate SLOs of the API; 429 shows the rate limiter at work. |

Also watch the database itself: disk, replication, connections, vacuum and bloat (PostgreSQL), compaction, repair, tombstones and hot partitions (Cassandra).

## Failure modes

| Symptom | Cause | What to do |
|---|---|---|
| Worker exits with a *chain divergence* | A block does not link to the stored cursor (archive served another chain, or the database holds a different history). Indexing stopped on purpose, nothing is written on top (ADR 0002). A message that another writer moved the cursor first means two writers raced; when it follows a lost lease the replica simply goes back to standby and does not exit. | Check `Algorand:GossipHosts` and `Algorand:ExpectedGenesisHash` and the node. If the database is wrong, reindex into a fresh one. If it names another writer, look for a second deployment on the same database. |
| Worker exits with *Balance underflow in round N* and the status stays on `Backfilling` or `Following` | The indexed balance of an account is lower than the chain's, so an earlier effect was missed or counted wrongly (the chain itself never overdraws). The log line `Indexing of <chain> stopped after round <R> ... : <reason>` carries the round, transaction id, effect, token, account, counterparty, the indexed balance and the missing amount (addresses in Algorand text form). Retrying cannot help; the pod restarts and stops again. | Compare the account's history for the token with an indexer of the chain (`/v2/accounts/{address}/transactions?asset-id=...`) to find the credit that is missing in `/v1/{chain}/accounts/{address}/tokens/{kind}/{id}/transactions`, fix the normalizer with a test for that era and reindex, because everything indexed with the defect is wrong (issue #115: asset close-outs before the protocol that records `aca`). |
| Worker exits after many transient failures (algod answering `403`, `429` or `5xx` counts, because public gateways throttle that way) | `Indexer:MaxConsecutiveFailures` consecutive failures without progress (archive or algod unreachable, or a block the archive never serves). | Fix the source; the orchestrator restarts the pod and indexing resumes from the cursor. Algod answers `403`, `408`, `429` and `5xx` are retried with a backoff of 2 s doubling to 1 min, so a worker that is blocked for good exits only after about 25 minutes with the defaults; `400`, `401` and `404` are fatal at once. A persistent `403` is a gateway block or a bad credential rather than load: check `Algorand:AlgodUrl`, the token and the allow-list, or use your own node. |
| `avm.worker.is_leader` is 0 on all replicas | Database unreachable, or a lease that has not expired yet. PostgreSQL releases the lease when the dead session is dropped (about 30 s with the lease session keepalives); Cassandra after `Storage:Cassandra:LeaseTtlSeconds`. | Wait one TTL; check database reachability. |
| Repeated lease losses | Cassandra timeouts or an unstable PostgreSQL connection. | Raise `Storage:Cassandra:LeaseTtlSeconds` (a lease renewal must fit well inside it) or fix the network; check `ReadTimeoutSeconds` is not set below what the cluster can do. |
| `503` "took too long" on holders | A holders-at-round request exceeded `ReadTimeoutSeconds`. | Query the latest round, a smaller page, or use an asynchronous scan. |
| `503` "chain has not been indexed yet" | The worker has not published the first round (new deployment or empty database). | Wait for the genesis allocation; check the worker log. |
| `429` | Rate limit per client IP. | Raise `Api:RateLimitPerMinute` or fix the client; behind an ingress make sure forwarded headers are trusted so clients are not merged. |
| Scan stays `Pending` | No worker replica runs, or the scan tables do not exist yet (the worker creates them). | Check the worker; scans of a replica that died are taken over after `Scans:StaleAfter`. |
| Scan `Failed` | A step kept timing out, or an unexpected error (see the worker log for the scan id). | Retry later or ask for a smaller token or the latest round. |
| API `500` mentioning a missing column | The API was upgraded before the worker applied the new schema. | Upgrade the worker (see the upgrade rules), the API recovers without restart. |

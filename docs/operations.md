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

The API is the one component that may scale with load: `api.autoscaling.enabled` gives it a HorizontalPodAutoscaler on CPU
(`minReplicas`, `maxReplicas`, `targetCPUUtilizationPercentage`; needs metrics-server and `api.resources.requests.cpu`). The rate limit
is per pod, so the limit a single client sees grows with the number of replicas. Keep `minReplicas` at 2 or more while the
PodDisruptionBudget is on (the chart refuses a minimum not above `minAvailable`). Switching autoscaling on for a running release removes
the replica count from the API Deployment, so Kubernetes falls back to 1 replica until the autoscaler (within a minute) scales it up again:
do it outside busy hours. The worker (one writer) and the embedded Cassandra (one data directory) do not scale.

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
- Backfill speed depends on the archival source and `Indexer:FetchConcurrency`. Public archival nodes answer `429` when they are asked too much: the fetcher then slows that node down on its own (per-node concurrency limit, `Retry-After`, see `Algorand:MaxConcurrentRequestsPerHost` in the README), so the `Gossip nodes are rate limiting` summary (about one per `Algorand:ThrottleLogInterval`, a minute) is not an error; a fetch only fails after `Algorand:ThrottleTimeout` of continuous rate limiting, which means the nodes cannot carry the load: lower `Indexer:FetchConcurrency` or use your own nodes. The worker keeps `Indexer:BalanceCacheEntries` account balances in memory (default
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

**Find the first affected round.** Every time a worker becomes the writer it records one row: the first round it will write, its build version and the time (issue #182).
`GET /v1/{chain}/status` shows the newest one as `indexedBy`; the full list is a query on the database (rounds from a row's `from_round` up to the next row's `from_round - 1` were written by that build):

- PostgreSQL: `SELECT * FROM indexing_term ORDER BY from_round;`
- Cassandra: `SELECT * FROM <keyspace>.indexing_term WHERE chain = '<chain>';` (newest first)

The versions are the image tags (ADR 0014). A deployment upgraded to this release gets its first row at the then indexed round + 1; rounds before that row were written by an unknown build.
A restart of the same build without new rounds adds no row.

**Partial rebuild, the default way to roll out a normalizer fix (issue #179, ADR 0021).** Only the rounds after the first affected round F are indexed again: the worker copies the old keyspace up
to R = F - 1 into the new one and indexes from R + 1 with the fixed code. Nobody touches the cluster; it is three changes of the values:

1. *Fill.* `storage.cassandra.keyspace: <new>`, `storage.cassandra.forkFrom: { keyspace: <old>, round: R, into: <new> }` and `storage.cassandra.apiKeyspace: <old>`. The worker copies the old keyspace
   (it logs `Partial rebuild of ...` every 30 seconds and exposes `avm.rebuild.ranges_done` / `avm.rebuild.ranges_total`), then replays the rounds after R. Meanwhile the API keeps serving the old keyspace, frozen
   at its last round (the status reports a growing lag), and new holder scans are answered with 503 (`Api:ScansEnabled` is switched off; existing scans stay readable). The deploy job prints the worker's report
   (step *Partial rebuild*); a refused request stops the worker with exit code 1 and the reason in its log (a wrong source, a keyspace that holds something else, a source built from another history).
   Take the progress of the new keyspace from the worker: its `avm.indexer.lag_rounds` metric or its log, because the public status still shows the old keyspace.
2. *Switch.* When the new keyspace follows the chain and the comparison of step 3 below is done, remove `apiKeyspace`. The API reads the new keyspace from then on. Answers for explicit rounds are cacheable for an
   hour (`Cache-Control` of the round endpoints), so wrong answers for rounds after R can stay in caches for that hour after the switch.
3. *Clean up.* `retiredKeyspaces: [<old>]` and remove `forkFrom`, in the same change (a later change of `keyspace` to yet another name with `forkFrom` still set would fork again from the old source; the keyspace the
   API reads, `apiKeyspace`, can never be retired, the chart and the worker refuse it).

The cost: the copy is a full read of the old keyspace's effect tables (the scan is by token range, the rounds after R are read and dropped on the client), then one lookup per account and token for the holdings. The
request is stored in the new keyspace: another source or round with the same keyspace name is refused ("use a new keyspace name"), and `forkFrom` staying in the values after the copy is harmless (the worker answers *already done*).

**Cassandra: fill a new keyspace from the old one (issue #178, ADR 0021).** Instead of indexing from genesis, a new keyspace takes over everything of the old one up to a round R
(`Storage:Cassandra:ForkFrom:Keyspace`, `:Round` and `:Into`, which must name the new keyspace so that a request left in the values cannot fill another one) and indexing continues after R.
The old keyspace is only read. The copy scans the old keyspace in 256 token ranges per table, filters by round on the client, derives the holdings from the newest copied effect of every
account and token, and publishes the cursor last (a lightweight transaction), so nothing is visible before the end. Progress is kept in `fork_progress`: a copy that stopped is repeated
from its finished ranges. The new keyspace is bound to its request: another source or round on the same keyspace is refused ("use a new keyspace name"), and a keyspace that already follows the
chain without a matching request is refused too. Rows of a round that was never published are not taken over (R must not exceed the source's published round). The worker
starts it (see above).

**PostgreSQL: rewind in place (issue #180, ADR 0021).** PostgreSQL has transactions, so a database is rewound to a round R in one transaction instead of being rebuilt next to the old one.
The provider supports it with two settings, `Storage:PostgreSql:RewindTo:Round` and `Storage:PostgreSql:RewindTo:RequestId` (use a new request id for every rewind: it is recorded in
`maintenance_request` in the same transaction, so the rewind runs once even when the values stay in the configuration). The rewind deletes the effects after R, recomputes the holdings
and the touched-account directory of the affected accounts from what remains, restores tokens (created after R: deleted; destroyed after R: alive again), deletes scans and build records after R,
and sets the cursor to R with state `Backfilling`; readers see the old state until the commit. Before you run it take a backup or note the PITR position (there is no undo window), expect clients
to see `indexedRound` drop from the tip to R (requests for rounds above R are refused as not indexed until the replay passes them), and expect the delete to take time on a big database
(it reads the affected pairs from the token-oriented table, then deletes by primary-key prefix). A request that is refused (the database is behind the round, or the hash differs) is not recorded and stays armed:
remove the setting after the run, or a later restart at a higher round would carry it out. The worker logs a Warning audit line when a rebuild ran; the chart values for the PostgreSQL rewind are not part of #179 (they follow when a PostgreSQL deployment needs them).

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
4. Reclaim the space once nobody needs the old data: add the old keyspace to `storage.cassandra.retiredKeyspaces` in the same values file and merge it (a database change request, ADR 0020).
   No cluster access is needed. After the deployment the worker that holds the writer lease drops the keyspace as soon as the status is `Following` (it checks once a minute, `Worker:RetirementCheckSeconds`) and logs
   `Retired storage <name> was removed`; the deploy job prints that line. It refuses a keyspace that holds another chain or is not an indexer keyspace (`was not removed`, the step *Database change requests* of the deploy job is marked failed), and waits
   (`still in use`) while a writer holds a lease in it or its status was updated in the last hour (`Storage:Cassandra:RetiredKeyspaceQuietMinutes`): a keyspace is dropped one hour after its last writer stopped at the earliest,
   which is also the time left to roll a switch back (`storage.cassandra.retiredKeyspaceQuietMinutes` in the values). Readers and a writer that has been failing for longer than that are not detected, so list a keyspace only when no release (API included) is configured with it any more.
   A keyspace without any published round is refused as well (drop such an empty one by hand).
   An entry whose keyspace is gone is harmless (`does not exist`); remove it with the next change. The drop ends the rollback option of step 1
   (the old name would be recreated empty). Cassandra keeps a snapshot of a dropped keyspace, which still holds its disk space: with the embedded Cassandra set `cassandra.embedded.autoSnapshotTtl`
   (for example `7d`; changing it restarts the Cassandra pod once) so that the snapshot, the last way back, expires by itself; on an external cluster set `auto_snapshot_ttl` or run `nodetool clearsnapshot`.
   Both keyspaces share the node's disk (`cassandra.embedded.hostPath`) while they coexist: check the free space before a rebuild of a large chain.

Which deployments need it after #115: every database that indexed an asset close-out (`aclose`) of protocols v0 to v24. On TestNet the range that was indexed (rounds 0 to 4,982,988)
contains such close-outs. MainNet ran the same early protocols and is affected as soon as its indexed range holds one asset close-out with an amount: list them with the Nodely indexer
(`GET https://mainnet-idx.4160.nodely.dev/v2/transactions?tx-type=axfer&min-round=A&max-round=B&limit=1000`, keep `asset-transfer-transaction.close-to` set and `close-amount` above 0; a block of a protocol
before v25 has no `aca`). Each such transaction proves the missing credit for its `close-to` account, and that account is the one to compare in step 3. Voi launched on a recent protocol and
is expected to be unaffected; this was not verified.

## Monitoring and alerts

Set `telemetry.otlpEndpoint` (ADR 0011). With the Prometheus Operator, `alerts.enabled` in the release values installs the alerts below as a
`PrometheusRule` (thresholds `alerts.lagRounds`, `statusAgeSeconds`, `api5xxRatio`, `apiP95Seconds`; extra labels for your Prometheus's rule
selector in `alerts.labels`). Every rule selects its release by `avm_chain="<chain>"`: promote the OTLP resource attribute **avm.chain** to a
label (Prometheus `otlp.promote_resource_attributes: [avm.chain]`, or the collector's resource-to-label processor), or set `alerts.selector`
to the matcher that identifies the release's series. The rules: `AvmIndexerLagHigh`, `AvmIndexerStalled`, `AvmIndexerNoWriter`,
`AvmIndexerTwoWriters`, `AvmIndexerLeaseFlapping`, `AvmIndexerStatusUnreadable`, `AvmIndexerDecoderErrors`, `AvmApiErrors`, `AvmApiSlow`
(tested with `promtool` in CI, `deploy/helm/ci/alerts.test.yaml`). It needs exported metrics (`telemetry.otlpEndpoint` or `otlpMetricsEndpoint`;
the chart refuses `alerts.enabled` without). Metrics ingested over OTLP carry no staleness markers, so a dead pod's last sample stays
visible for Prometheus's lookback (5 minutes by default): a no-writer or stalled page can come up to that much later than its `for` delay.
The lag alert needs 30 minutes of history, so a release that has just started is not judged yet; the API rules leave out the `/healthz` probes. The metrics:

| Metric | Alert |
|---|---|
| `avm.indexer.lag_rounds` | Warn when it stays above a few hundred rounds and does not shrink (`AvmIndexerLagHigh`: the freshest replica's value above `alerts.lagRounds` for 15 minutes while not below its lowest value of the half hour before, so a backfill that keeps catching up, at any speed, does not alert). |
| `avm.indexer.status_age_seconds` | Critical above a few minutes: the writer has not updated the status (stuck, no leader, database away). |
| `avm.indexer.indexed_round` | Should rise by about one round per few seconds on MainNet; flat means stuck. |
| `avm.indexer.status_sample_failures` | Rises while a replica cannot read the database; the other `avm.indexer.*` gauges are then stale. |
| `avm.audit.last_round`, `avm.audit.rounds`, `avm.audit.mismatches`, `avm.audit.ignored_mismatches`, `avm.audit.skipped` | The ledger audit (ADR 0021, below): the last round compared with the ledger, how many, how many disagreed (the worker stops at the first when `worker.audit.stopOnMismatch`), mismatches at rounds you listed in `ignoreRounds`, and rounds or passes it could not compare (no deltas on the node, node or database unreachable: never a failure). `avm.audit.last_round` falling behind `avm.indexer.indexed_round` while following means the node is slow or has no deltas. |
| `avm.worker.is_leader` | The sum over live replicas should be 1. Alert when it is 0 for longer than the lease time plus a minute (`AvmIndexerNoWriter`: at least 3 minutes, Cassandra lease TTL plus 60 s when longer; also when no worker exports at all). A sum of 2 can appear for a moment while an old pod's last sample is still visible: alert only when it lasts (`AvmIndexerTwoWriters`: 10 minutes). |
| `avm.worker.leases_lost` | Alert on a rate above a few per hour (flapping connection or Cassandra timeouts). |
| `avm.worker.leases_acquired` | A step of 1 is a failover; many in a short time is flapping. |
| `avm.worker.transient_failures` | Sustained growth means the archive, algod or the database is unhealthy. |
| `avm.worker.rounds_committed` | Throughput of this replica; zero on the leader while lag is large means stuck. |
| `avm.indexer.commit.duration` (histogram, s), `avm.indexer.commit.effects` | Commit latency and effects written by the writer for the rounds after genesis (the genesis round is one write of its own); p95 well above a second while following means a slow database. Rounds are `avm.worker.rounds_committed`. |
| `avm.fetch.block.duration{source}` (histogram, s), `avm.fetch.block.failures{source,reason}` | Latency of blocks that were served, per source (`archival`, `relay`), and failed requests per source and reason (`throttled`, `not_found`, `invalid`, `network`); slow failures show up as `network` (timeouts), not in the latency. Archival `throttled` or `network` rising is the usual cause of a slow backfill; relay failures are expected and harmless. |
| `avm.indexer.decoder_errors{kind=block\|arc200_log}` | `block`: a fetch failed and most nodes that answered served a block that does not validate (a bad fleet, or a block this decoder cannot read); alert on any. `arc200_log`: contracts emitting a non-standard transfer event in published rounds, informational. |
| `avm.scan.duration{outcome=completed\|failed\|abandoned}` (histogram, s) | Duration of one replica's run of a holder scan (a scan resumed by another replica, after an outage or a takeover, is one `abandoned` and one later sample); `failed` rising means scans end with an error that retrying does not fix (most often the storage read timeout on a large token; the worker log line *Scan ... failed* has the cause). |
| ASP.NET Core `http.server.request.duration`, Kestrel and runtime metrics | Latency and error-rate SLOs of the API; 429 shows the rate limiter at work. |

Also watch the database itself: disk, replication, connections, vacuum and bloat (PostgreSQL), compaction, repair, tombstones and hot partitions (Cassandra).

### Ledger audit (issue #181, ADR 0021)

The writer compares the rounds it has published with the node's own post-round balances (`GET /v2/deltas/{round}`): the end-of-round native and asset balances of every account the ledger changed in that round must be what the
database holds for that round (absolute, pinned to the round; ARC-200 balances are application state whose layout differs per contract, so they are not audited). While the worker follows the tip one round in `worker.audit.followSampleEvery` is audited
(default 10, one request of 30 to 70 KB each; 1 audits every round and is meant for an own node, see *Daily quota of a public node* below); while it backfills, and during the replay after a partial rebuild, one round in `worker.audit.backfillSampleEvery` (default 1000, at most one request per pass), which is what can catch an
era-specific bug near the round where it happens. The audit runs after publication: it detects a bad round, it does not keep it from being served. It is one-directional: it reads the accounts the ledger lists for the round, so a wrong effect for an account the ledger did not change is only found at the next round in which the ledger lists that account. A node that does not answer, answers 404 for a round, or sends a document that
cannot be read is counted as skipped and logged at most every ten minutes; it is never a failure. A node that has no deltas for 20 rounds in a row at the start (Voi, a private node) switches the audit off with one log line.
A node that answers 403 or 429 (throttled, or its daily quota is used up) pauses the audit for 15 minutes with one warning; it then continues at the round published at that time, the rounds in between are not audited.

#### Daily quota of a public node (issue #219)

The built-in node URLs are the free Nodely endpoints. Their [fair usage policy](https://nodely.io/docs/free/policy) allows 200,000 requests or 5 GB per day per IP and site (128,000 requests and 2 GB from 2027-01-01),
counted per UTC day; near the quota a growing share of requests is answered `403 Daily free API quota exceeded`, after it all of them until 00:00 UTC, and refused requests count too.
A following worker asks for the status every `Indexer:FollowPollInterval` (2 s, about 45,000 to 70,000 requests a day on Algorand) and for the deltas of the audited rounds (every round would be about 30,000 requests and 1 to 2 GB a day, which is what
used the TestNet quota up after #181). The worker treats the quota answer as expected: it logs *The node of ... answered that its daily request quota is used up* (one line, Warning), waits `Indexer:QuotaBackoff` (1 minute) and tries again,
without counting towards `Indexer:MaxConsecutiveFailures`, so it does not restart; the status goes stale meanwhile and the staleness alert fires. If it happens every day: point `Algorand:AlgodUrl` at an own node or a paid plan,
raise `worker.audit.followSampleEvery`, or set it to 0. A 403 that does not name the quota (a blocked IP, a bad token) is still a failure that stops the worker after the retries.

On a mismatch the worker logs the round, account, token, indexed and ledger amounts (Error), records the stop in the database (`audit_stop`), and, with `worker.audit.stopOnMismatch` (default true), stops with exit code 1 and the line
*Ledger audit of ... failed at round S ...; this writer has published up to T; the last round that agreed is S - 1*. The stop survives restarts: a recorded, unacknowledged stop makes every worker refuse to index (the same message).
What to do: take S from the log, find the cause, fix the normalizer with a test for that era, then rebuild from a round below S with the partial rebuild runbook above (the PostgreSQL rewind and a new Cassandra keyspace both release
the stop). When you decided that the ledger side is the one that is wrong (a difference of the oracle), set `worker.audit.acknowledgeRound: S` instead: the worker acknowledges it and indexes on; `worker.audit.ignoreRounds: [S]` makes a
known round only a warning (counted in `avm.audit.ignored_mismatches`). MainNet ships with `stopOnMismatch: false` until TestNet has shown no false alarm for two weeks; the first thing to check after a mismatch there is whether it is real.
To read the stops: PostgreSQL `SELECT * FROM audit_stop ORDER BY round;`, Cassandra `SELECT * FROM <keyspace>.audit_stop WHERE chain = '<chain>';`.

## Failure modes

| Symptom | Cause | What to do |
|---|---|---|
| Worker exits with a *chain divergence* | A block from the archival nodes does not link to the stored cursor (archive served another chain, or the database holds a different history). A relay block that does not link cannot cause it: it is replaced by the archival one and the relay is rested (Warning 1117). A relay block that links but is forged (certificates are not checked, ADR 0004) is indexed, and the next archival block then does not link: the divergence names that round, and the relay is not blamed. Indexing stopped on purpose, nothing is written on top (ADR 0002). A message that another writer moved the cursor first means two writers raced; when it follows a lost lease the replica simply goes back to standby and does not exit. | Check `Algorand:GossipHosts` and `Algorand:ExpectedGenesisHash` and the node. If the database is wrong, reindex into a fresh one. If it names another writer, look for a second deployment on the same database. |
| Worker exits with *Ledger audit of ... failed at round S* and starts and exits again | The ledger audit found that the database disagrees with the node's ledger deltas at round S (the log line names account, token, indexed and ledger amount) and recorded the stop, so no replica indexes past it. | Fix the cause and rebuild from a round below S (partial rebuild runbook), or acknowledge the round with `worker.audit.acknowledgeRound` when the ledger side is wrong (section *Ledger audit*). |
| Worker exits with *Balance underflow in round N* and the status stays on `Backfilling` or `Following` | The indexed balance of an account is lower than the chain's, so an earlier effect was missed or counted wrongly (the chain itself never overdraws). The log line `Indexing of <chain> stopped after round <R> ... : <reason>` carries the round, transaction id, effect, token, account, counterparty, the indexed balance and the missing amount (addresses in Algorand text form). Retrying cannot help; the pod restarts and stops again. | Compare the account's history for the token with an indexer of the chain (`/v2/accounts/{address}/transactions?asset-id=...`) to find the credit that is missing in `/v1/{chain}/accounts/{address}/tokens/{kind}/{id}/transactions`, fix the normalizer with a test for that era and reindex, because everything indexed with the defect is wrong (issue #115: asset close-outs before the protocol that records `aca`). |
| Warning *Relay <host> served a block <round> that does not link to the chain the archival nodes serve* | A relay served a valid block of another history or a fork at the tip. The writer used the archival block of that round instead; the relay is left out like in the next row. Logged at most once an hour per relay (shared with the next row). | Nothing for indexing. If it repeats for the same relay, report it to the network's operators. |
| Warning *Relay <host> served block <round> that failed validation* | A relay of the public network served a corrupt block or one of another chain, while another node served a valid block of that round. Indexing is not affected, and the relay is left out for ten times the host cooldown (capped at an hour, never shorter than the cooldown). Logged at most once an hour per relay; each invalid answer is also a Warning *Fetching block ... failed*. Many relays at once can also mean they run a node release this indexer cannot read yet. | Nothing for indexing. If it repeats for the same relay, report it to the network's operators; `Algorand:GossipHosts` (your own archival nodes) skips relay discovery entirely (ADR 0019). |
| Worker exits after many transient failures (algod answering `403`, `429` or `5xx` counts, because public gateways throttle that way) | `Indexer:MaxConsecutiveFailures` consecutive failures without progress (archive or algod unreachable, or a block the archive never serves). | Fix the source; the orchestrator restarts the pod and indexing resumes from the cursor. Algod answers `403`, `408`, `429` and `5xx` are retried with a backoff of 2 s doubling to 1 min, so a worker that is blocked for good exits only after `Indexer:MaxConsecutiveFailures` attempts (about 25 minutes with the defaults); every other algod status (such as `400`, `401`, `404`) is fatal at once. A persistent `403` is a gateway block or a bad credential rather than load: check `Algorand:AlgodUrl`, the token and the allow-list, or use your own node. |
| Warning *algod reports round N as the latest since T; the chain or the node is stuck* and `/status` turns `Stalled` | algod's latest round did not advance for `Indexer:TipStaleAfter`, so the idle writer stopped refreshing the status (an Information line says when it advances again). | Check the algod node's catch-up (`/v2/status`, its logs, its peers); the chain itself rarely stops. |
| `avm.worker.is_leader` is 0 on all replicas | Database unreachable, or a lease that has not expired yet. PostgreSQL releases the lease when the dead session is dropped (about 30 s with the lease session keepalives); Cassandra after `Storage:Cassandra:LeaseTtlSeconds`. | Wait one TTL; check database reachability. |
| Repeated lease losses | Cassandra timeouts or an unstable PostgreSQL connection. | Raise `Storage:Cassandra:LeaseTtlSeconds` (a lease renewal must fit well inside it) or fix the network; check `ReadTimeoutSeconds` is not set below what the cluster can do. |
| `503` "took too long" on holders | A holders-at-round request exceeded `ReadTimeoutSeconds`. | Query the latest round, a smaller page, or use an asynchronous scan. |
| `503` "chain has not been indexed yet" | The worker has not published the first round (new deployment or empty database). | Wait for the genesis allocation; check the worker log. |
| `429` | Rate limit per client IP. | Raise `Api:RateLimitPerMinute` or fix the client; behind an ingress make sure forwarded headers are trusted so clients are not merged. |
| Scan stays `Pending` | No worker replica runs, or the scan tables do not exist yet (the worker creates them). | Check the worker; scans of a replica that died are taken over after `Scans:StaleAfter`. |
| Scan `Failed` | A step kept timing out, or an unexpected error (see the worker log for the scan id). | Retry later or ask for a smaller token or the latest round. |
| API `500` mentioning a missing column | The API was upgraded before the worker applied the new schema. | Upgrade the worker (see the upgrade rules), the API recovers without restart. |

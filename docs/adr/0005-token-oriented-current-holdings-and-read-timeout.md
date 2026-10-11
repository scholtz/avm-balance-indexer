# ADR 0005: Token-oriented current holdings and a read timeout

- Status: accepted
- Date: 2026-10-11
- Issue: #22

## Context

Holders of a token at a round were always answered by walking every account that ever touched the token and looking up
its latest balance at or before the round. That is right for any past round, but for the indexed round (the common
question: who holds this token now) the answer already exists as the current holdings, only indexed by account, not by token.
Separately, a slow scan could hold a snapshot and a connection for minutes.

## Decision

- Keep a second copy of the current holdings keyed by token: `holdings_by_token` (PostgreSQL primary key
  `(token_kind, token_id, account)`; Cassandra partition `(token_kind, token_id, shard)` clustered by account, the same
  account hash sharding as the other token tables). The round writer maintains it next to `holdings_by_account` (same
  transaction in PostgreSQL; same round timestamp and the same unpublished-row rule in Cassandra, see ADR 0003).
- `GetHoldersAtRound` for `round == indexed round` is one ordered range scan of that table. Any older round keeps the
  per-account lookup. The result is identical by construction and the shared contract suite compares both paths.
- PostgreSQL databases created before the table existed are backfilled once by schema script `0002` when the table is empty.
  Cassandra has no deployments that predate the table; a keyspace created earlier must be replayed.
- Upgrade procedure: the backfill copies what is in `holdings_by_account` when the schema is applied, so the single writer
  must not be running an older version meanwhile (stop the old worker, then start the new one, which applies the schema first).
  Schema scripts run without a command timeout because the backfill can copy a large table.
- The index also stores `opted_in`, so latest and historical holders agree (historical reads take it from the latest effect at or
  before the round). PostgreSQL adds the column with migration `0003`, which fills it from `holdings_by_account`, and holds the single
  backfill; Cassandra adds it with `0002_*.cql` (`ADD IF NOT EXISTS`), which the schema manager applies when the column is missing, and
  rows without the flag are resolved from the effect history. The upgrade rule above applies to this migration too: no writer of the
  previous version may run while it is applied, and the API must not be upgraded before a new worker has applied the schema (the
  latest-holders query selects the new column).
- Reads have a timeout: PostgreSQL `SET LOCAL statement_timeout` per read snapshot (the connection's command timeout is raised above it), Cassandra a
  deadline over the whole read plus the driver request timeout.
  Both surface as `StorageTimeoutException` and HTTP 503, so one request cannot hold a snapshot for minutes.
  Asynchronous scans for past rounds of very large tokens are tracked in #30.

## Consequences

- One more row write per touched account and round in both providers (the account-oriented holdings write is doubled).
- The PostgreSQL timeout is per statement, so a request made of several statements can exceed it in total; the Cassandra
  deadline covers the whole read.

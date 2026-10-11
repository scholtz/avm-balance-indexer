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
- Reads have a timeout: PostgreSQL `SET LOCAL statement_timeout` per read snapshot, Cassandra the driver request timeout.
  Both surface as `StorageTimeoutException` and HTTP 503, so one request cannot hold a snapshot for minutes.
  Asynchronous scans for past rounds of very large tokens are tracked in #30.

## Consequences

- One more row write per touched account and round in both providers (the account-oriented holdings write is doubled).
- The timeout is per statement in PostgreSQL, so a request made of several statements can exceed it in total.

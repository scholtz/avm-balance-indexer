# ADR 0003: Cassandra round commit without cross-partition transactions

- Status: accepted
- Date: 2026-10-11
- Issue: #14

## Context

PostgreSQL commits a round's effects, holdings and publication cursor in one transaction. Cassandra has no
transaction across partitions, yet readers must never see a half-written round and the Cassandra provider must pass
the same contract suite (`StorageContractTests`) as PostgreSQL.

## Decision

- **Publication is the commit.** A round is written as idempotent upserts into every query table, awaited to
  completion, and then published with a lightweight transaction on `chain_status`:
  `UPDATE ... IF indexed_round = <previous> AND indexed_block_hash = <previous hash>` (`INSERT ... IF NOT EXISTS` for
  round 0). Losing the compare-and-set raises `ChainDivergenceException`, so two writers racing on one round cannot
  both publish. All reads are bounded by the published round, and the cursor is read with `LOCAL_SERIAL` so a
  published cursor is never read stale.
- **Replay converges.** Keys are deterministic `(round, intra_round_order, ordinal)`, so a crash before publication
  leaves rows that replaying the same round overwrites with identical values. Duplicate effect identities in one
  round are rejected before anything is written (an upsert would merge them silently).
- **Write order inside a round:** effects and bucket index rows, then `accounts_touched_by_token` rows for accounts
  that had no holding row, then holdings. A holding row therefore implies its touched row, which keeps `first_round`
  correct across a crash and replay without a read-modify-write.
- **Holdings are a cache of the effect history.** They are written with `USING TIMESTAMP = round + 1`, so a stale
  writer replaying an older round can never overwrite a newer round. A holding whose `last_changed_round` is beyond the
  published round (left by a crashed or losing writer) is ignored and resolved from the effect history instead, so the
  balance tracker never builds on an unpublished balance.
- **Query tables** (`Schema/0001_initial.cql`): `effects_by_account_token_bucket` (account history and "latest effect
  at or before R", newest first, clustering `(round, intra_round_order, ordinal)` all descending, partitioned by
  account/token/100,000-round bucket), `effect_buckets_by_account_token` (which buckets contain effects, so a balance
  lookup skips empty ones), `effects_by_token_bucket` and `accounts_touched_by_token` (both sharded by an FNV hash of
  the account into 16 shards, fixed in the on-disk layout like `RoundBuckets.Size`).
- **Holders at a round** scan the 16 touched-account shards lazily in account order, look up each account's latest
  balance at or before the round, and merge the shards, so the cost follows the page size and not the holder count.
  Cursors are identical to PostgreSQL's (hex account, `round:order:ordinal`, `kind:id`).
- Optional columns (`counterparty`, `inner_path`) are left out of the insert instead of bound to null, because a null
  cell is a tombstone and about every other effect has no counterparty.
- The keyspace is created by the schema manager (`NetworkTopologyStrategy`, configurable replication factor) unless
  `Storage:Cassandra:CreateKeyspace` is false. Reads and writes use `LOCAL_QUORUM`.

## Consequences

- Current-holdings and effect rows of an unpublished round can exist for a short time or after a crash; they are
  invisible to every read except as described for holdings above.
- `GetHoldingsAsync` filters zero balances in the client (amounts are text), so a page can cost several partition reads
  for an account with many zero-balance opt-ins.
- Fee sink and rewards pool remain hot partitions (see ADR 0002); the 100,000-round bucket and the shard count need
  load testing before MainNet.
- Concurrent schema changes from several pods rely on `IF NOT EXISTS` and the driver's schema agreement wait; run the
  schema step from a single pod (the worker) as with PostgreSQL.

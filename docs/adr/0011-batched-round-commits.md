# ADR 0011: Batched round commits while backfilling

- Status: accepted
- Date: 2026-10-11
- Issue: #15

## Context

Every round was its own database transaction: about ten statements and round trips plus a cursor update, 2.5 to 3 ms against a
local PostgreSQL (330 rounds/s) and far more over a network, regardless of how few effects the round had. Most of a backfill is
small rounds. The benchmark (`BackfillBenchmark`) also showed that block download is the other limit (CPU is about 2 ms per round).

## Decision

- `IRoundWriter.WriteRoundsAsync` writes several consecutive rounds as one unit and publishes the last. Nothing of a batch is visible
  before that, so "an API never sees a half-written round" still holds, and the cursor never points into the middle of a batch.
  PostgreSQL merges the batch into one transaction with one statement per table (per-effect round and bucket arrays, the newest
  effect per holding, first/last round per touched account); Cassandra writes the rounds in order and does a single compare-and-set
  publication per batch; a batch of one round behaves exactly like `WriteRoundAsync` (idempotent replay included).
- `RoundIndexer` stages rounds while backfilling (more than `FollowingThresholdRounds` behind the target) and flushes at
  `CommitBatchRounds` rounds, `CommitBatchEffects` effects, the target, or when the archive has a hole. Near the tip every round is
  published on its own.
- `BalanceTracker` keeps the balances of staged rounds in an overlay the next round reads, and moves them to the cache only after the
  batch is durable. A failed write throws out of `RunAsync`; the tracker is discarded with it.
- Token records are still written when a round is staged (before its batch is published), so a destroy later in the same batch finds the
  record. Replaying a batch rewrites identical values: creation builds a fresh record, destroy and reconfiguration use only fields that never change after creation (creator, total, url, issuing application), so a record written ahead of a failed batch does not change how earlier rounds replay.
- A failing fetch or bad block writes the rounds already computed (within 10 s) before the error is reported; cancellation does not, because it also means a lost writer lease.
- `FetchConcurrency` defaults to 64 (was 8).

## Consequences

- After a crash up to a batch of rounds is computed again (at most 25 rounds, or 20,000 effects).
- Progress, the published cursor and `RoundsCommitted` advance in steps of a batch while backfilling.

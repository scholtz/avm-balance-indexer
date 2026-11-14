# 0021 Partial rebuild instead of a block archive

Status: accepted (issue #176; implementation issues link here)

## Context
A fix in the normalizer or the balance tracker only changes rounds indexed afterwards. Rows already written cannot be edited
in Cassandra (ADR 0003), so until now a fix meant a rebuild from genesis into a new keyspace: TestNet took a month after #115.
The product description asked for an own archive of raw blocks to make such rebuilds cheaper (#174). The owner rejected that:
the chain's archival nodes already are the immutable source, and a second copy next to them is storage and operations for
nothing. What is wanted is that a fix rarely needs a rebuild, and never one from genesis.

Two facts make that possible:

- Every effect row carries its round, and everything else is derived from effects: current holdings are a cache of the effect
  history (ADR 0003), the touched-account directory has `first_round`, the bucket index follows from the effects.
- A bug affects rounds from some first round on. Everything indexed before it is correct and can be kept.

## Decision
- **No block archive.** Blocks are always read from the chain's archival nodes (and relays near the tip, ADR 0019).
- **Partial rebuild ("fork at a round").** The release values name a source and a round R, the last round known to be good.
  The data up to and including R is taken over, and indexing continues from R + 1 with the fixed code:
  - Cassandra: a new keyspace is filled from the old one. Effects with `round <= R` are copied, the bucket index and the
    touched-account rows follow from them, holdings are written from the newest copied effect of each account and token (with
    the same write timestamp rule as the round writer), token records created by R are copied with a destruction after R
    undone, and the status row for R is published last. The old keyspace is not modified; it keeps serving until the switch and is
    retired afterwards (ADR 0020).
  - PostgreSQL: the same result is reached in place and in one transaction: rows after R are deleted, holdings are recomputed
    for the accounts those rows touched, the status is set to R. Each request carries an id and runs once.
- **The publication rule holds.** The copy is invisible until its status row exists; a crashed copy is repeated (idempotent
  upserts) or resumed. The hash of block R comes from one fetch of that block, and the first indexed round checks its
  `previous hash` against it as always.
- **It is a database change request** like a retired keyspace (ADR 0020): requested in the values by pull request, carried
  out by the worker that holds the writer lease, reported by the deploy job.
- **Finding R early.** Two additions keep R recent and known:
  - a ledger audit at the tip: the writer compares the balances of each round with the ledger deltas of the node
    (`/v2/deltas/{round}`, Algorand networks) and stops at the first round that disagrees;
  - the database records which build indexed which round range, so "everything indexed by a build before the fix" is a query,
    not archaeology.

## Consequences
- A fix whose first affected round is recent costs a copy (hours) plus the rounds after R, instead of the whole chain. A bug
  in an early protocol era (as #115) still needs most of the chain; the audit only sees the tip, so era bugs are a matter of
  tests (`NormalizerEraTests`), not of this mechanism.
- Token records are not versioned. A record changed after R (reconfiguration, ARC-20 classification) keeps its newer values
  in the copy until the replay passes that round again. Balances do not depend on those fields (the normalizer needs only
  the creator and the total, which never change).
- Choosing R is a human decision; a wrong R (later than the first bad round) keeps bad rows. The runbook says how to find
  it, and the audit and the build record help.
- The copy doubles the disk use of the kept range until the old keyspace is retired.
- Voi has no ledger deltas; its audit is limited to what the node offers (current account state).

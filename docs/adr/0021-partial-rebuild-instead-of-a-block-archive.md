# 0021 Partial rebuild instead of a block archive

Status: accepted as the design (issue #176). Not implemented yet: #178 (Cassandra copy), #179 (values, worker, pipeline,
runbook), #180 (PostgreSQL), #181 (ledger audit), #182 (build record). Until #179 is merged the runbook in
`docs/operations.md` still describes the rebuild from genesis.

## Context
A fix in the normalizer or the balance tracker only changes rounds indexed afterwards. Rows already written cannot be edited
in Cassandra (ADR 0003), so until now a fix meant a rebuild from genesis into a new keyspace: TestNet took a month after #115.
The product description asked for an own archive of raw blocks to make such rebuilds cheaper (#174). The owner rejected that:
the chain's archival nodes already are the immutable source, and a second copy next to them is storage and operations for
nothing. What is wanted is that a fix rarely needs a rebuild, and never one from genesis.

Two facts make that possible:

- Every effect row carries its round, and balances, holdings, the touched-account directory and the bucket index are derived
  from effects (holdings are a cache of the effect history, ADR 0003).
- A bug affects rounds from some first round on. Everything indexed before it is correct and can be kept.

## Decision
- **No block archive.** Blocks are always read from the chain's archival nodes (and relays near the tip, ADR 0019).
- **Partial rebuild ("fork at a round").** The release values name a source and a round R, the last round known to be good.
  The data up to and including R is taken over, and indexing continues from R + 1 with the fixed code.
  - Cassandra: a new keyspace is filled from the old one. Effects with `round <= R` are copied, the bucket index follows from
    them, touched-account rows with `first_round <= R` are copied, holdings are written from the newest copied effect of each
    account and token (with the write timestamp rule of the round writer), token records created by R are copied with a
    destruction after R undone, and the status row for R is published last. The old keyspace is never modified and is retired
    afterwards (ADR 0020).
  - PostgreSQL: the same state is reached in place and in one transaction: effect rows after R are deleted, holdings are
    recomputed for the accounts and tokens those rows touched, touched-account rows first seen after R are deleted (and
    `last_round` recomputed), token records created after R are deleted and a destruction after R undone, scan jobs for
    rounds after R are deleted, the status is set to R. Each request carries an id and runs once.
- **Preconditions, checked before anything is written.** The source holds this chain; R is not beyond the source's published
  round (rows of an unpublished round can exist, ADR 0003, and must never be taken over); the target is empty, or is the
  unfinished copy of the same source and the same R (the request is stored in the target; a copy started with another R is
  refused, never continued: its rows and holding timestamps would outlive the correction).
- **The hash of R is confirmed before publication.** `chain_status` only stores the hash of the tip, so the hash of block R
  comes from a fetch. Block R + 1 is fetched as well and must name that hash as its previous block; only then is the status row
  written. The first indexed round checks its previous hash against the stored one as always.
- **Readers.** The copy is invisible until its status row exists, and reads of a keyspace without one fail. Therefore the API
  does not move with the worker: the values give the API its own keyspace setting, which stays on the old keyspace (frozen at
  its last round, consistent, with a growing lag) until the new one follows the chain, and is switched in a second change. In
  PostgreSQL readers see the state before the rewind until the transaction commits; afterwards `indexedRound` is R and
  requests for later rounds are refused as not indexed until the replay passes them.
- **The writer's memory.** The rewind or copy runs after the writer lease is taken and before the indexer is seeded for that
  term, so no balance cache, token cache, rewards state or cursor of an earlier term survives it.
- **It is a database change request** like a retired keyspace (ADR 0020): requested in the values by pull request, carried
  out by the worker that holds the writer lease, reported by the deploy job.
- **Finding R early.**
  - Ledger audit: the writer compares balances with the ledger deltas of the node (`/v2/deltas/{round}`, Algorand networks):
    every round while following the tip, and a sample of rounds during backfill and during the replay after a partial rebuild,
    as far as the node serves deltas for them. The audit runs after publication: it detects, it does not prevent. A node that
    is unavailable, rate-limited or answers something unreadable is a skipped round, never a failure. A disagreement stops the
    writer at that round (configurable per release), because every further round would have to be rebuilt.
  - The database records which build indexed which round range, so "everything indexed by a build before the fix" is a lookup.

## Consequences
- A fix whose first affected round is recent costs a copy (a full read of the old effect tables, hours) plus the rounds after
  R, instead of the whole chain. A bug in an early protocol era (as #115) still needs most of the chain.
- **What is guaranteed equal** to a database indexed straight through with the fixed code: effects, balances at every round,
  holdings and holders. **Token records are not versioned** and are outside that guarantee: a field changed after R
  (reconfiguration, ARC-20 classification, ARC-200 record) keeps its value in the copy unless the replay writes that token
  again. Balances do not depend on those fields (the normalizer needs only creator and total, which never change). A fix that
  concerns token records themselves needs R before the creation of the affected tokens.
- Choosing R is a human decision; an R later than the first bad round keeps bad rows. The audit and the build record help.
- PostgreSQL has no undo window: the rows after R are gone at commit. Take a backup or note the PITR position before the
  request. Cassandra keeps the old keyspace until it is retired.
- Asynchronous holder scans (ADR 0012) are not carried over to a new keyspace: a scan that is polled across the API switch
  ends with "not found" and has to be started again.
- The copy doubles the disk use of the kept range until the old keyspace is retired.
- Cassandra's lightweight-transaction publication and the lease are unchanged; the copy holds the writer lease for hours and
  stops when it is lost.
- Voi has no ledger deltas; its audit is off until another oracle exists.

# 0021 Partial rebuild instead of a block archive

Status: accepted as the design (issue #176). Not implemented yet: #178 (Cassandra copy), #179 (values, worker, pipeline,
runbook), #180 (PostgreSQL), #181 (ledger audit), #182 (build record),
#168 (the status reports a frozen keyspace as stalled). Until #179 is merged the runbook in
`docs/operations.md` still describes the rebuild from genesis.

## Context
A fix in the normalizer or the balance tracker only changes rounds indexed afterwards. Rows already written cannot be edited
in Cassandra (ADR 0003), so until now a fix meant a rebuild from genesis into a new keyspace: TestNet took a month after #115.
The product description asked for an own archive of raw blocks to make such rebuilds cheaper (#174). The owner rejected that:
the chain's archival nodes already are the immutable source, and a second copy next to them is storage and operations for
nothing. What is wanted is that a fix rarely needs a rebuild, and then only of the rounds from the first bad one.

Two facts make that possible:

- Every effect row carries its round, and balances, holdings, the touched-account directory and the bucket index are derived
  from effects (holdings are a cache of the effect history, ADR 0003).
- A bug affects rounds from some first round on. Everything indexed before it is correct and can be kept.

## Decision
- **No block archive.** Blocks are always read from the chain's archival nodes (and relays near the tip, ADR 0019).
- **Partial rebuild ("fork at a round").** The release values name a source, a round R (the last round known to be good) and
  the target the request is meant for; a request whose target is not the database the worker uses is refused, so a request
  left in the values can never fire again for a later keyspace.
  The data up to and including R is taken over, and indexing continues from R + 1 with the fixed code.
  - Cassandra: a new keyspace is filled from the old one. Effects with `round <= R` are copied, the bucket index follows from
    them, touched-account rows with `first_round <= R` are copied, holdings are written from the newest copied effect of each
    account and token (with the write timestamp rule of the round writer), token records created by R are copied with a
    destruction after R undone, and the status row for R is published last. The old keyspace is never modified and is retired
    afterwards (ADR 0020).
  - PostgreSQL: the same state is reached in place and in one transaction: effect rows after R are deleted, holdings are
    recomputed for the accounts and tokens those rows touched (and deleted where no effect at or before R remains),
    touched-account rows first seen after R are deleted (and `last_round` recomputed), token records created after R are
    deleted and a destruction after R undone, scan jobs for rounds after R are deleted, an audit stop after R is cleared,
    build records are cut at R, and the status is set to R. Each request carries an id and runs once.
- **A request that was carried out does nothing.** The request stays in the values across restarts, so the first check is
  whether it was carried out: the target has a published round and holds this request (source and R; stored in the target
  when the copy starts), or the request id is recorded (PostgreSQL). Then nothing is read or written, also when the source no
  longer exists. A target with a published round but without this request (a mistyped name, an indexed keyspace) is refused,
  and so is a source equal to the target.
- **Preconditions, checked before anything is written.** Both providers: the database holds this chain, and R is not beyond
  its published round (rows of an unpublished round can exist, ADR 0003, and must never be taken over). Cassandra in addition:
  the source differs from the target, and the target is empty or is the unfinished copy of the same source and the same R (the
  request is stored in the target; a copy started with another R is refused, never continued: its rows and holding timestamps
  would outlive the correction). PostgreSQL rewinds the database it uses, so source and target are the same by design.
- **The source is checked against the chain before anything is copied.** `chain_status` only stores the hash of the source's
  tip T. Block T is fetched and its hash must equal the stored one; blocks are hash-linked, so this shows the source was built
  from the chain the nodes serve, R included. In both providers the hash of R written into the status comes from fetching block
  R, and the first replayed round checks its previous hash against it as always.
- **Readers.** The copy is invisible until its status row exists, and reads of a keyspace without one fail. Therefore the API
  does not move with the worker: the values give the API its own keyspace setting, which stays on the old keyspace until the
  new one follows the chain, and is switched in a second change. The keyspace the API reads can never be retired (ADR 0020's
  guard covers it like the worker's keyspace). The old keyspace is frozen: consistent up to its last round,
  but its status row is no longer updated, so the status keeps its last round and lag while `updatedAt` ages (the status
  reports that as stalled, #168). Holder scans are executed by the worker, which is on the new keyspace: while the API is on
  the old one it refuses to create scans instead of accepting jobs nobody runs, and jobs that were pending or running in the
  old keyspace when the worker moved are never finished; they end as expired. In
  PostgreSQL readers see the state before the rewind until the transaction commits; afterwards `indexedRound` is R and
  requests for later rounds are refused as not indexed until the replay passes them.
- **The writer's memory.** The rewind or copy runs after the writer lease is taken and before the indexer is seeded for that
  term, and the indexer's process-wide caches (balances, known ARC-200 tokens, rewards state, cursor) are reset, so nothing
  learned in an earlier term survives it.
- **It is a database change request** like a retired keyspace (ADR 0020): requested in the values by pull request, carried
  out by the worker that holds the writer lease, reported by the deploy job.
- **Finding R early.**
  - Ledger audit: the writer compares balances with the ledger deltas of the node (`/v2/deltas/{round}`, Algorand networks):
    every round while following the tip, and a sample of rounds during backfill and during the replay after a partial rebuild,
    as far as the node serves deltas for them. The audit runs after publication: it detects, it does not prevent. A node that
    is unavailable, rate-limited or answers something unreadable is a skipped round, never a failure. A disagreement stops the
    writer (configurable per release), because every further round would have to be rebuilt. The audit runs after
    publication and batches publish several rounds at once, so the writer stops at its current tip, which can be past the bad
    round S; a rebuild must choose R below S. The stop is recorded in the database, so a restarted worker does not index on
    until an operator acknowledges it or a partial rebuild releases it. Native and ASA balances are audited; ARC-200 balances
    live in application state whose layout differs per contract, so they are not audited.
  - The database records which build indexed which round range, so "everything indexed by a build before the fix" is a lookup.
    A partial rebuild takes the records up to R over (Cassandra copies them, PostgreSQL cuts them at R), so the history of the
    kept rounds is not lost with the old keyspace. An audit stop is not taken over: the rebuild releases it.

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
  request. Cassandra keeps the old keyspace until it is retired. The rewind deletes by round from a table that is keyed and
  partitioned by account, so it reads every partition; a rewind over many rounds is one long transaction (WAL, bloat, vacuum
  held back). Its cost has to be measured before PostgreSQL is used for a large chain (#180).
- Responses for an explicit round are cacheable for an hour (`Cache-Control: public, max-age=3600`, `ReadEndpoints.MarkImmutable`). Answers for rounds after R
  given before the switch (Cassandra) or the rewind (PostgreSQL) can stay in client and proxy caches for that hour.
- This mechanism is for fixes of the indexer's own logic. A database built from a wrong history (a hash divergence) has no
  stored per-round hashes to find a good round by; that case stops the writer and is decided by an operator.
- A rebuild still reads the replayed rounds from the archival nodes. Voi's archival hosts are patchy and Voi has no ledger
  audit, so on Voi the protection is weaker: tests and the build record only.
- Asynchronous holder scans (ADR 0012) are not carried over to a new keyspace: a scan that is polled across the API switch
  ends with "not found" and has to be started again.
- The copy doubles the disk use of the kept range until the old keyspace is retired.
- Cassandra's lightweight-transaction publication and the lease are unchanged; the copy holds the writer lease for hours and
  stops when it is lost.
- Voi has no ledger deltas; its audit is off until another oracle exists.

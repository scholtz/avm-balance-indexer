# ADR 0012: Asynchronous holder scans

- Status: accepted
- Date: 2026-10-11
- Issue: #30

## Context

Holders at the indexed round are one index scan (ADR 0005), but a past round of a very large token (native ALGO on MainNet) is answered
account by account, and a client that wants every holder pays for thousands of paged requests, each of which can time out. Operators
also want one consistent export, not a moving window.

## Decision

- A scan is a durable job: `POST .../holders/scans` stores a `Pending` job (token, round, include-zero, expiry) and answers `202`.
  The round is fixed when the scan is accepted (default: the published round), so the result is one consistent snapshot.
- **Every worker replica executes scans**, not only the writer lease holder: a scan reads chain data and writes only its own result.
  Replicas claim jobs from a shared queue; a job has one owner, and every owner action is conditional on still being the owner of a
  running job. PostgreSQL claims with `FOR UPDATE SKIP LOCKED`; Cassandra with lightweight transactions on the job row.
- **Bounded memory, resumable.** The executor reads `PageSize` holders at a time through the normal holders-at-round query (so every
  step is subject to the read timeout) and appends them with the resume cursor in one step. A running job whose owner has not
  checkpointed for `StaleAfter` is claimed by another replica and continues from the stored cursor; result rows are numbered from the
  stored count, so a retried step rewrites the same keys.
- **Readable while running.** Result pages are numbered rows; a page of an unfinished scan is final, only more rows can follow.
  `complete` is true for a finished scan on its last page.
- **Admission and retention.** At most `Scans:MaxActive` active scans (`429` + `Retry-After`); a job and its result are deleted after
  `Scans:Retention`, by the workers.
- **Cassandra:** every write to a job row (create, claim, checkpoint, finish, cancel, delete) is a lightweight transaction.
  Paxos writes carry server-side timestamps; mixing them with client-timestamped writes lets a plain write shadow a conditional one when
  the clocks differ (observed with a Docker VM clock), so the row never sees a plain write. Results live in numbered buckets of 1000 rows.
- Transient storage errors retry the step (`MaxStepRetries`); anything else fails the scan with a generic message (details stay in the
  worker log).

## Consequences

- A scan of a token with N holders costs about N rows of storage until it expires.
- Cancelling takes effect at the next checkpoint; a step already read is wasted.
- Results are not compressed or exported as files; a client reads the pages.

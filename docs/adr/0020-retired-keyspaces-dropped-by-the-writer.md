# 0020 Database change requests: retired keyspaces are dropped by the writer

Status: accepted (issue #160)

## Context
A normalizer fix is rolled out by rebuilding into a new keyspace (ADR 0003, reindex runbook in `docs/operations.md`). The old
keyspace then had to be dropped by hand with `cqlsh` on the Cassandra pod. That needs cluster access nobody working through
pull requests has (issue #119 waited a month for it), it is not reviewed, and nothing checks that the keyspace typed into the
shell is the right one.

## Decision
- The request is configuration: `storage.cassandra.retiredKeyspaces` in the release values
  (`Storage:Cassandra:RetiredKeyspaces`). It is changed by pull request and reaches the cluster with the normal deployment.
- The worker carries it out (`RetiredStorageCleaner`), not the pipeline: it already has the database connection and the
  rights, it works for an external Cassandra too, and the pipeline needs no `pods/exec` right. Only the replica that holds the
  writer lease acts, and only when the chain status of the keyspace in use is `Following`, so a rebuild that is still
  backfilling never loses the data it replaces. A failure is retried and never stops indexing.
- A drop cannot be undone, so the provider refuses anything that is not plainly ours to drop:
  the active keyspace, `system*` keyspaces and invalid names fail the options validation and, before that, the chart render
  (a pod that cannot start would stop indexing); a keyspace without `chain_status` and `writer_lease` tables, or with a
  `chain_status` row of another chain, is reported as refused. A missing keyspace is "absent", so the request is idempotent
  and the entry may stay listed.
- "Nobody writes to it" needs more than one look, because the lease row of a restarting writer is missing for a moment. A
  keyspace is in use while any lease row exists in it (of any chain: a keyspace that has not published a round yet does not
  say whose it is) or while its status was updated within the quiet period (`Storage:Cassandra:RetiredKeyspaceQuietMinutes`,
  60 by default; a writer keeps the status moving even when idle). It is then tried again at the next check. The quiet period
  is also the time in which a switch to a new keyspace can still be rolled back.
- Each keyspace is its own request: it is logged as soon as it is done, and a refused or finished one is not asked again.
- The deploy job reads the logs of all worker pods after the upgrade and reports each request; a refused one marks its step
  failed with an error annotation, without failing the deployment of this or the following chains.
- Cassandra snapshots a dropped keyspace (`auto_snapshot`), which keeps its disk space. The embedded Cassandra of the chart
  can expire such snapshots (`cassandra.embedded.autoSnapshotTtl`, written into `cassandra.yaml` at start because the image
  has no setting for it). The snapshot is the undo window of a drop.

## Consequences
- Listing a keyspace is the approval to delete it. The comparison of the rebuilt data with the chain (runbook step 3) stays
  a human check before the entry is added; the worker only verifies `Following`.
- Readers are invisible: an API that still serves from a retired keyspace (the two-release rebuild of a healthy chain) holds
  no lease and writes nothing. List a keyspace only when no release is configured with it any more.
- PostgreSQL has no equivalent (a rebuild there uses another database); the provider registers no `IRetiredStorage`.
- Setting `autoSnapshotTtl` changes the Cassandra pod and restarts it once. An external Cassandra needs
  `auto_snapshot_ttl` (or `nodetool clearsnapshot`) from its operator.
- Schema changes inside a keyspace keep their existing path (numbered `Schema/*.cql` scripts applied by the worker at start).

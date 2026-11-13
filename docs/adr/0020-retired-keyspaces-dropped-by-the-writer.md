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
  the active keyspace and `system*` keyspaces fail the options validation (and the chart render); a keyspace without
  `chain_status` and `writer_lease` tables, or with a `chain_status` row of another chain, is reported as refused; a keyspace
  whose writer lease row is alive (another release still indexes into it) is left and tried again later. A missing keyspace
  is "absent", so the request is idempotent and the entry may stay listed.
- The deploy job reads the worker log after the upgrade and reports each request; a refused one fails the job.
- Cassandra snapshots a dropped keyspace (`auto_snapshot`), which keeps its disk space. The embedded Cassandra of the chart
  can expire such snapshots (`cassandra.embedded.autoSnapshotTtl`, written into `cassandra.yaml` at start because the image
  has no setting for it). The snapshot is the undo window of a drop.

## Consequences
- Listing a keyspace is the approval to delete it. The comparison of the rebuilt data with the chain (runbook step 3) stays
  a human check before the entry is added; the worker only verifies `Following`.
- PostgreSQL has no equivalent (a rebuild there uses another database); the provider registers no `IRetiredStorage`.
- Setting `autoSnapshotTtl` changes the Cassandra pod and restarts it once. An external Cassandra needs
  `auto_snapshot_ttl` (or `nodetool clearsnapshot`) from its operator.
- Schema changes inside a keyspace keep their existing path (numbered `Schema/*.cql` scripts applied by the worker at start).

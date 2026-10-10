# ADR 0001: Dual storage providers behind one contract

- Status: accepted
- Date: 2026-10-10
- Issue: #1

## Context

The product must run on either PostgreSQL or Apache Cassandra, chosen independently per chain deployment, so that
benchmark results can drive the final choice without a code change. The two engines have fundamentally different
data models (relational with joins/transactions vs. query-first partitioned tables with idempotent upserts).

## Decision

- A provider-neutral storage contract lives in `AvmBalanceIndexer.Domain.Storage` (`IChainStatusStore`, `ITokenStore`,
  `IBalanceReadStore`, `IRoundWriter`, `ISchemaManager`).
- Each provider is a separate project with its own hand-written schema (`Schema/*.sql`, `Schema/*.cql`) and query
  implementation. No shared ORM, no lowest-common-denominator schema.
- `Storage:Provider` selects exactly one provider at startup; the other is never opened. Switching providers is a
  redeploy plus replay, never a dual-write.
- Contract guarantees shared by both: canonical decimal amount encoding, end-of-round semantics, deterministic
  `EffectId`, idempotent replay, identical pagination and the publication rule (a round is visible only after all of
  its rows are committed).
- The same contract test suite runs against both providers in CI.

## Consequences

- Two implementations to maintain; mitigated by the shared contract suite.
- Cassandra needs the explicit round-publication workflow because it has no multi-partition transaction.
- The API and indexer are provider-agnostic and ship as one image containing both drivers.

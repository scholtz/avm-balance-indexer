# Contributing

## Workflow

1. Open or pick a GitHub issue; every change references one.
2. Branch from `main`: `feature/<issue>-<slug>` or `fix/<issue>-<slug>`.
3. Keep PRs focused. CI must be green: build, unit tests, the storage contract suite (in-memory reference and
   PostgreSQL), the live-network job, Docker builds and Helm lint.
4. Squash-merge into `main`.

## Code

- .NET 10, nullable and implicit usings on, warnings are errors (`Directory.Build.props`).
- Package versions are managed centrally in `Directory.Packages.props`; never put a `Version` on a `PackageReference`.
- Amounts are `Amount` (BigInteger-backed). Never use `long`, `double` or `decimal` for token quantities.
- Anything that differs between PostgreSQL and Cassandra lives inside the provider project. The Domain contract
  must stay provider-neutral; do not leak SQL/CQL concepts into it.
- Storage behaviour changes need a test in the shared contract suite
  (`tests/AvmBalanceIndexer.TestSupport/StorageContractTests.cs`). Every provider derives from it, so one test covers all.
- Balance logic changes must keep `AvmBalanceIndexer.Live.Tests` green: it compares every account in every one of the
  first 1000 rounds of TestNet and MainNet with the ledger itself.
- Network access in tests belongs only in `AvmBalanceIndexer.Live.Tests`; set `AVM_SKIP_LIVE_TESTS=1` to skip it.
- Architectural decisions are recorded in `docs/adr/` (one file per decision, numbered).

## Local setup

```sh
docker compose up -d postgres
dotnet build AvmBalanceIndexer.slnx
dotnet test  AvmBalanceIndexer.slnx   # starts its own PostgreSQL container unless AVM_TEST_POSTGRES is set
```

## Commit messages

Conventional style, imperative mood, reference the issue: `feat(storage): add PostgreSQL round writer (#12)`.

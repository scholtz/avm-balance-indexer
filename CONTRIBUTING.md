# Contributing

## Workflow

1. Open or pick a GitHub issue; every change references one.
2. Branch from `main`: `feature/<issue>-<slug>` or `fix/<issue>-<slug>`.
3. Keep PRs focused. CI must be green: build, unit tests, storage contract suite for **both** providers, Docker
   builds and Helm lint.
4. Squash-merge into `main`.

## Code

- .NET 10, nullable and implicit usings on, warnings are errors (`Directory.Build.props`).
- Package versions are managed centrally in `Directory.Packages.props`; never put a `Version` on a `PackageReference`.
- Amounts are `Amount` (BigInteger-backed). Never use `long`, `double` or `decimal` for token quantities.
- Anything that differs between PostgreSQL and Cassandra lives inside the provider project. The Domain contract
  must stay provider-neutral; do not leak SQL/CQL concepts into it.
- Storage behaviour changes need a contract test that runs against both providers.
- Architectural decisions are recorded in `docs/adr/` (one file per decision, numbered).

## Local setup

```sh
docker compose up -d
dotnet build AvmBalanceIndexer.slnx
dotnet test  AvmBalanceIndexer.slnx
```

## Commit messages

Conventional style, imperative mood, reference the issue: `feat(storage): add PostgreSQL round writer (#12)`.

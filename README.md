# AVM Balance Indexer

Self-hosted indexer and API for Algorand-family (AVM) token balances. Indexes a chain from genesis, follows new
rounds, and serves current and end-of-round balances, holders and per-token account history for native ALGO/VOI,
ASAs, ARC-20 Smart ASAs and ARC-200 application tokens.

Full design: [PRODUCT-DESCRIPTION.md](PRODUCT-DESCRIPTION.md).

## Highlights

- **One deployment per chain** (`algorand-testnet`, `algorand-mainnet`, `voi`) with isolated cursor, storage and API.
- **Two storage providers** behind one contract: PostgreSQL (Npgsql + Dapper) and Apache Cassandra (DataStax driver),
  selected per deployment by `Storage:Provider`. Same effects in, identical API results out.
- **No snapshots.** Immutable per-effect rows carry `balance_after`, so "balance at round R" is a latest-row lookup;
  historical holder lists are computed by streaming token effects.
- **Exact arithmetic.** `Amount` wraps `BigInteger`; amounts travel as canonical decimal strings.
- **Kubernetes native.** Helm chart with per-chain value overlays; databases provisioned by operators.

## Repository layout

```
src/
  AvmBalanceIndexer.Domain/              token/effect model, Amount, storage contract interfaces
  AvmBalanceIndexer.Indexing/            chain adapters, effect normalization, ingestion
  AvmBalanceIndexer.Storage.PostgreSql/  provider + Schema/*.sql
  AvmBalanceIndexer.Storage.Cassandra/   provider + Schema/*.cql
  AvmBalanceIndexer.Api/                 ASP.NET Core /v1/{chain} read API
  AvmBalanceIndexer.Worker/              indexer worker (single ordered writer per chain)
tests/
  AvmBalanceIndexer.Domain.Tests/        unit tests
  AvmBalanceIndexer.Storage.ContractTests/ same suite run against both providers in CI
deploy/helm/                             chart + stage-testnet / prod-algorand / prod-voi overlays
docker/                                  Dockerfile.api, Dockerfile.worker
docs/adr/                                architecture decision records
```

## Getting started

Requires the .NET 10 SDK and Docker.

```sh
docker compose up -d                       # local PostgreSQL + Cassandra
dotnet build AvmBalanceIndexer.slnx
dotnet test  AvmBalanceIndexer.slnx
dotnet run --project src/AvmBalanceIndexer.Api
curl http://localhost:5000/v1/algorand-testnet/status
```

Provider selection and connection settings live under the `Storage` section of `appsettings*.json` or the matching
`Storage__*` environment variables. Only the selected provider is initialised.

## API (v1)

| Endpoint | Purpose |
|---|---|
| `GET /v1/{chain}/status` | indexed round, target round, lag, ingestion state |
| `GET /v1/{chain}/accounts/{address}/tokens` | current holdings (`includeZero` for opted-in zero balances) |
| `GET /v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/balance?round=R` | end-of-round balance |
| `GET /v1/{chain}/tokens/{kind}/{tokenId}/holders?round=R` | paginated end-of-round holders |
| `GET /v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/transactions` | account/token effect history |
| `GET /v1/{chain}/tokens/{kind}/{tokenId}` | token metadata |

Every response carries `indexedRound`; requests for rounds beyond it are rejected explicitly.

## Status

Project scaffolding (issue #1). Storage implementations, ingestion and endpoints are tracked in follow-up issues.

## License

[MIT](LICENSE)

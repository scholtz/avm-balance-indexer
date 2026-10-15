# AVM Balance Indexer Go SDK: instructions for AI coding assistants

Use this file when you write code that reads token balances, holders or history of Algorand MainNet, Algorand TestNet or Voi through `github.com/scholtz/avm-balance-indexer/sdk/go`.
You can paste the "Project rules" block into the `AGENTS.md` / `CLAUDE.md` / `.cursorrules` of the project that uses the SDK.

## What the service is

An HTTP API (`/v1/{chain}/…`) that indexes every round of a chain and answers: current holdings of an account, **balance of an account for a token at the end of any round**,
holders of a token at a round, effect history of an account and token, token metadata. Tokens: native (`avm.Algo`, id 0), ASA (`avm.Asa`, asset id; ARC-20 Smart ASAs are ASAs) and ARC-200 (`avm.Arc200`, application id).
It does **not** sign or send transactions and does not read the chain for you: use `go-algorand-sdk` for that.

## Project rules (copy into the consuming project)

```
- Read Algorand/Voi balances, holders and history with github.com/scholtz/avm-balance-indexer/sdk/go (import avm); never call the HTTP API by hand and never scrape node REST endpoints for this.
- Create one client per chain: avm.New(avm.AlgorandMainnet|avm.AlgorandTestnet|avm.Voi, nil). Only set Options.BaseURL for a self-hosted deployment. Reuse the client; it is safe for concurrent use.
- Balances and deltas are decimal strings: parse with big.Int.SetString(s, 10), never float64. Divide by 10^decimals only for display (decimals come from Token()).
- avm.Algo is the native token with token id 0. ARC-200 ids are application ids. Ids and rounds are uint64; optional parameters are pointers (nil = omitted).
- For all holders of a token use ForEachHolder (pins the round) or, for very large tokens or past rounds, ExportHolders. Never loop with a hand-made cursor.
- Always pass a context with a deadline. Handle *avm.APIError via errors.As (Status, IsNotFound, IsRetryable, RetryAfter). Do not retry blindly; honour RetryAfter.
- Do not invent methods or endpoints; the full list is below.
```

## Cheat sheet

```go
import avm "github.com/scholtz/avm-balance-indexer/sdk/go"

indexer, _ := avm.New(avm.Voi, nil)                                        // default public deployment of the chain
own, _ := avm.New(avm.Voi, &avm.Options{BaseURL: "https://indexer.example.com", Headers: map[string]string{"X-Api-Key": key}})

indexer.Status(ctx)                                                        // .State .IndexedRound (*uint64) .TargetRound .Lag
indexer.Balance(ctx, address, avm.Asa, 31566704, nil)                      // latest published round; .Amount (string) .Round .IndexedRound
round := uint64(40_000_000); indexer.Balance(ctx, address, avm.Asa, 31566704, &round)
indexer.AccountTokens(ctx, address, nil)                                   // one page: .Items .NextCursor (*string)
indexer.Token(ctx, avm.Arc200, 1234567)                                    // .Symbol (*string) .Name .Decimals (*int32) .Standard …
indexer.Holders(ctx, avm.Asa, 31566704, &avm.GetTokenHoldersParams{Limit: &limit})   // one page
indexer.ForEachHolder(ctx, avm.Asa, 31566704, nil, func(h avm.HolderDto) error { return nil })    // all pages, round pinned
indexer.ForEachHistory(ctx, address, avm.Algo, 0, nil, func(e avm.EffectDto) error { return nil }) // newest first: .Round .Type .Delta .BalanceAfter .TransactionId
indexer.ExportHolders(ctx, avm.Algo, 0, avm.ExportOptions{Round: &round}, func(h avm.HolderDto) error { return nil }) // any size, via a server-side scan
```

## Recipes

- **Balance of one account**: `b, err := indexer.Balance(...)`, then `new(big.Int).SetString(b.Amount, 10)`. A missing holding is a balance of `"0"`, not an error.
- **Top holders**: collect with `ForEachHolder` into a slice and `sort.Slice` by `big.Int` amount descending; holders come in account order, there is no server-side sorting.
- **Is the data fresh?** `State` is `Following` at the chain tip, `Backfilling` while catching up (`Lag` rounds behind); `NotStarted`, `Bootstrapping`, `Replaying`, `Failed` otherwise.
  Answers are always consistent as of `IndexedRound`.
- **A round beyond `IndexedRound`** gives `*avm.APIError` with `Status` 404 (`requestedRound` and `indexedRound` in `Body`); wait or use a lower round.
- **Effects** (`History`): `Type` is one of `Payment`, `Fee`, `Reward`, `CloseOut`, `AssetTransfer`, `AssetOptIn`, `AssetOptOut`, `AssetCreate`, `AssetDestroy`, `AssetClawback`, `Arc200Transfer`, `Arc200Mint`, `Arc200Burn`, `FeeCollected`, `ProposerPayout`, `Genesis`, `RewardsWithdrawal`, `LedgerAdjustment`.
  `InnerPath` is non-empty for effects of inner transactions.

## Pitfalls

- Several fields are pointers because the API can return `null` (`IndexedRound` before the first round is indexed, token `Symbol`/`Name`/`Decimals` for tokens without metadata): check for `nil`.
- `Limit` is 1 to 1000. `Cursor` is opaque: pass back exactly what `NextCursor` returned, together with the same `Round`. The `ForEach…` helpers copy the params you pass; they do not modify them.
- A client is bound to one chain. Asking a deployment for another chain returns 404 ("This deployment does not serve that chain").
- Rate limit of the public deployments: 600 requests per minute and client address. Prefer `Limit` 1000 over many small pages.
- Generated names follow oapi-codegen: `ChainId`, `TokenKind`, `GetTokenHoldersParams`, `Id` (not `ID`) fields.
- `generated.go` has exported identifiers such as `NewClient`, `WithBaseURL` and `GeneratedClient` (the raw client); the SDK entry point is `avm.New`.

## Errors

`*avm.APIError`: `Status`, `Message`, `IsNotFound()`, `IsRetryable()` (429, 503, 504), `RetryAfter`, `Body`. `*avm.ScanError`: an export ended as Failed or Cancelled (`.Scan`).
Transport errors (refused connection, timeout, cancelled context) are returned wrapped with `%w`.

## Verify your work

Run `go vet ./... && go test -race ./...` in this module when you change the SDK; consumers can call `indexer.Status(ctx)` on the TestNet client as a smoke test.
API reference: `sdk/openapi/avm-balance-indexer.v1.json` (OpenAPI 3.0) and the running service at `/openapi/v1.json`. Overview of all SDKs: `sdk/README.md`; machine-readable summary: `docs/llms.txt`.

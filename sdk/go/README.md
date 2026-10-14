# AVM Balance Indexer: Go SDK

Typed client for the [AVM Balance Indexer](../../README.md): token balances, holders and history for **Algorand MainNet**, **Algorand TestNet** and **Voi**.
Native ALGO/VOI, ASAs (including ARC-20 Smart ASAs) and ARC-200 application tokens, at the end of any indexed round.

- Go 1.22+, standard `net/http`, `context` everywhere, one small dependency (`oapi-codegen/runtime`, for the UUID type).
- Request and response types and the raw HTTP client are **generated from the OpenAPI specification** of the service ([oapi-codegen](https://github.com/oapi-codegen/oapi-codegen)); a small hand-written layer adds defaults, pagination and errors.
- Works out of the box against the public deployments and against your own.

| Chain | `ChainId` | Default URL |
|---|---|---|
| Algorand MainNet | `avm.AlgorandMainnet` | https://mainnet.balance-indexer.biatec.io |
| Algorand TestNet | `avm.AlgorandTestnet` | https://testnet.balance-indexer.biatec.io |
| Voi | `avm.Voi` | https://voi.balance-indexer.biatec.io |

## Install

```sh
go get github.com/scholtz/avm-balance-indexer/sdk/go
```

```go
import avm "github.com/scholtz/avm-balance-indexer/sdk/go"
```

## Quick start

```go
ctx := context.Background()
indexer, err := avm.New(avm.Voi, nil) // or avm.AlgorandMainnet, avm.AlgorandTestnet
if err != nil { log.Fatal(err) }

status, err := indexer.Status(ctx)
fmt.Println(status.State, *status.IndexedRound, *status.TargetRound) // Following 12345 12346

// Balance of an account at the end of a round (nil round: the latest published one). Amounts are decimal strings.
round := uint64(40_000_000)
balance, err := indexer.Balance(ctx, "ACCOUNT…", avm.Asa, 31566704, &round)
amount, _ := new(big.Int).SetString(balance.Amount, 10)

// Every holder of a token, page by page.
err = indexer.ForEachHolder(ctx, avm.Asa, 31566704, nil, func(h avm.HolderDto) error {
    fmt.Println(h.Account, h.Amount)
    return nil // return an error to stop the walk
})
```

A runnable version is in [`examples/quickstart`](examples/quickstart/main.go).

## Choosing the deployment

If you do not set a URL, the public deployment of the chain is used. All options are optional (pass `nil` for none):

```go
indexer, err := avm.New(avm.AlgorandMainnet, &avm.Options{
    BaseURL:    "https://indexer.example.com",       // your own deployment, a gateway, or a local port-forward (no /v1/... suffix)
    Headers:    map[string]string{"X-Api-Key": key}, // for example an API key of your own gateway
    Timeout:    15 * time.Second,                    // default 30 s, negative disables
    HTTPClient: myClient,                            // anything with Do(*http.Request): proxies, tracing, retrying clients
})
```

A deployment of the indexer serves exactly one chain, so use one client per chain. A `Client` is safe for concurrent use.

## Reading data

| Method | Returns |
|---|---|
| `Status(ctx)` | ingestion state, indexed round, target round, lag |
| `AccountTokens(ctx, address, *GetAccountTokensParams)` | one page of the current holdings of an account |
| `Balance(ctx, address, kind, tokenID, round *uint64)` | balance at the end of a round (`Amount` is a decimal string) |
| `History(ctx, address, kind, tokenID, *GetTokenHistoryParams)` | one page of balance effects, newest first |
| `Holders(ctx, kind, tokenID, *GetTokenHoldersParams)` | one page of holders in account order |
| `Token(ctx, kind, tokenID)` | symbol, name, decimals, standard, creator |
| `ForEachAccountToken` / `ForEachHistory` / `ForEachHolder` | callback over all pages |
| `ExportHolders(ctx, kind, tokenID, ExportOptions, fn)` | callback over **every** holder of a token of any size (asynchronous scan) |
| `StartHolderScan` / `HolderScan` / `HolderScanPage` / `CancelHolderScan` | the scan API behind `ExportHolders` |

`kind` is `avm.Algo` (native, token id `0`), `avm.Asa` (asset id; ARC-20 Smart ASAs are ASAs) or `avm.Arc200` (application id). Ids and rounds are `uint64`. Optional parameters are pointers in the params structs (`nil` = not sent).
Balances are **always decimal strings**: parse with `big.Int.SetString(amount, 10)` and divide by `10^decimals` from `Token` for display.

```go
limit := int32(50)
page, err := indexer.AccountTokens(ctx, "ACCOUNT…", &avm.GetAccountTokensParams{Limit: &limit}) // page.Items, page.NextCursor
err = indexer.ForEachHistory(ctx, "ACCOUNT…", avm.Algo, 0, nil, func(e avm.EffectDto) error {
    fmt.Println(e.Round, e.Type, e.Delta, e.BalanceAfter)
    return nil
})
```

## Pagination and consistent reads

A page size (`limit`) outside 1 to 1000 is refused before anything is sent (TypeScript `RangeError`, Python `ValueError`, .NET `ArgumentOutOfRangeException`, Go `ErrInvalidArgument`, Rust `Error::InvalidArgument`).

List responses carry `IndexedRound` (the round the data is as of) and `NextCursor` (opaque, pass it back as `Cursor`; `nil` on the last page). The `ForEach…` helpers do this for you.
`ForEachHolder` without `Round` **pins the round of the first page** for all following pages, so the walk is consistent while the indexer advances. Set `Round` yourself to read a past state.

A past round of a very large token (for example the native token) can exceed the server read timeout (HTTP 503). Use `ExportHolders`, which runs an asynchronous scan on the server and streams the result:

```go
round := uint64(30_000_000)
err = indexer.ExportHolders(ctx, avm.Algo, 0, avm.ExportOptions{Round: &round, PollInterval: 2 * time.Second}, func(h avm.HolderDto) error {
    return nil
})
```

It returns a `*avm.ScanError` if the scan fails or is cancelled, and an `*avm.APIError` with status 429 when the server runs too many scans (retry later).

## Errors

```go
_, err := indexer.Token(ctx, avm.Asa, 123)

var apiError *avm.APIError
if errors.As(err, &apiError) {
    apiError.Status        // 404
    apiError.Message       // message from the API
    apiError.IsNotFound()  // unknown token, round beyond the indexed one, or wrong chain
    apiError.IsRetryable() // 429 / 503 / 504
    apiError.RetryAfter    // time.Duration from Retry-After, zero when absent
    apiError.Body          // raw body
}
if avm.IsNotFound(err) { /* shortcut */ }
```

Network failures and timeouts are returned wrapped (`errors.Is(err, context.DeadlineExceeded)` and `errors.As(err, &net.Error)` work). The SDK does **not** retry on its own; give `Options.HTTPClient` a retrying
client or honour `RetryAfter` yourself. The public deployments limit each client address to 600 data requests per minute.

## Generated code

`generated.go` holds the models (`StatusResponse`, `HolderDto`, `ScanResponse`, …), the enums `ChainId` and `TokenKind`, the params structs and the raw `GeneratedClient`. Use the raw client only if you need something the wrapper does not offer.

## Development

```sh
go vet ./... && go test -race ./...
```

`generated.go` is produced by `sdk/scripts/generate.sh` from `sdk/openapi/avm-balance-indexer.v1.json`; do not edit it. The Go module is versioned by git tag `sdk/go/vX.Y.Z`
(created by the publish workflow). See [sdk/README.md](../README.md) for regeneration, versioning and publishing.

## License

MIT

# AVM Balance Indexer: .NET SDK

Typed client for the [AVM Balance Indexer](../../README.md): token balances, holders and history for **Algorand MainNet**, **Algorand TestNet** and **Voi**.
Native ALGO/VOI, ASAs (including ARC-20 Smart ASAs) and ARC-200 application tokens, at the end of any indexed round.

- .NET 8 and .NET 10, nullable annotations, `async`/`IAsyncEnumerable`, no dependencies beyond the framework (`System.Text.Json`).
- The HTTP client and the models are **generated from the OpenAPI specification** of the service ([NSwag](https://github.com/RicoSuter/NSwag)); a small hand-written layer adds defaults, pagination and exceptions.
- Works out of the box against the public deployments and against your own.

| Chain | `ChainId` | Default URL |
|---|---|---|
| Algorand MainNet | `ChainId.AlgorandMainnet` | https://mainnet.balance-indexer.biatec.io |
| Algorand TestNet | `ChainId.AlgorandTestnet` | https://testnet.balance-indexer.biatec.io |
| Voi | `ChainId.Voi` | https://voi.balance-indexer.biatec.io |

## Install

```sh
dotnet add package AvmBalanceIndexer.Client
```

## Quick start

```csharp
using AvmBalanceIndexer.Client;
using AvmBalanceIndexer.Client.Generated;

using var indexer = AvmBalanceIndexerClient.Voi();   // or .AlgorandMainnet(), .AlgorandTestnet(), new AvmBalanceIndexerClient(options)

var status = await indexer.GetStatusAsync();
Console.WriteLine($"{status.State} {status.IndexedRound} {status.TargetRound}");   // Following 12345 12346

// Balance of an account at the end of a round (omit `round` for the latest published one). Amounts are decimal strings.
var balance = await indexer.GetBalanceAsync("ACCOUNT…", TokenKind.Asa, 31566704, round: 40_000_000);
Console.WriteLine(BigInteger.Parse(balance.Amount));

// Every holder of a token, page by page.
await foreach (var holder in indexer.EnumerateHoldersAsync(TokenKind.Asa, 31566704))
{
    Console.WriteLine($"{holder.Account} {holder.Amount}");
}
```

A runnable version is in [`examples/Quickstart`](examples/Quickstart/Program.cs).

## Choosing the deployment

If you do not set a URL, the public deployment of the chain is used. Everything below is optional:

```csharp
var indexer = new AvmBalanceIndexerClient(new AvmBalanceIndexerOptions
{
    Chain = ChainId.AlgorandMainnet,                       // which chain the deployment at BaseUrl serves (default: AlgorandMainnet)
    BaseUrl = new Uri("https://indexer.example.com"),      // your own deployment, a gateway, or a local port-forward (no /v1/... suffix)
    Timeout = TimeSpan.FromSeconds(15),                    // default 30 s (when the SDK creates the HttpClient)
    Headers = { ["X-Api-Key"] = key },                     // for example an API key of your own gateway
});
```

With `IHttpClientFactory` (retries, handlers, logging), pass the client in. The SDK never modifies it (no `BaseAddress`, no default headers, no timeout): the address, the headers and the user agent are applied to each request, so a shared or already used `HttpClient` is safe.
Timeouts and resilience then come from your client (`AddStandardResilienceHandler()` is in the `Microsoft.Extensions.Http.Resilience` package):

```csharp
services.AddHttpClient("indexer").AddStandardResilienceHandler();
// …
var client = new AvmBalanceIndexerClient(new() { Chain = ChainId.Voi, HttpClient = factory.CreateClient("indexer") });
```

The static factories (`AvmBalanceIndexerClient.Voi(options)`) copy your options object, so one `AvmBalanceIndexerOptions` can serve several chains.

A deployment of the indexer serves exactly one chain, so use one client per chain.

## Reading data

| Method | Returns |
|---|---|
| `GetStatusAsync()` | ingestion state, indexed round, target round, lag |
| `GetAccountTokensAsync(address, includeZero, cursor, limit)` | one page of the current holdings of an account |
| `GetBalanceAsync(address, kind, tokenId, round)` | balance at the end of a round (`Amount` is a decimal string) |
| `GetHistoryAsync(address, kind, tokenId, fromRound, toRound, cursor, limit)` | one page of balance effects, newest first |
| `GetHoldersAsync(kind, tokenId, round, includeZero, cursor, limit)` | one page of holders in account order |
| `GetTokenAsync(kind, tokenId)` | symbol, name, decimals, standard, creator |
| `EnumerateAccountTokensAsync` / `EnumerateHistoryAsync` / `EnumerateHoldersAsync` | `IAsyncEnumerable` over all pages |
| `ExportHoldersAsync(kind, tokenId, round, includeZero)` | `IAsyncEnumerable` over **every** holder of a token of any size (asynchronous scan) |
| `StartHolderScanAsync` / `GetHolderScanAsync` / `GetHolderScanPageAsync` / `CancelHolderScanAsync` | the scan API behind `ExportHoldersAsync` |

`TokenKind` is `Algo` (native, token id `0`), `Asa` (asset id; ARC-20 Smart ASAs are ASAs) or `Arc200` (application id). Ids and rounds are `ulong`.
Balances are **always decimal strings**: parse with `BigInteger.Parse` and divide by `10^decimals` from `GetTokenAsync` for display. All methods take a `CancellationToken`.

```csharp
var usdc = await indexer.GetTokenAsync(TokenKind.Asa, 31566704);                 // Symbol "USDC", Decimals 6, Standard "Asa"
var mine = await indexer.GetAccountTokensAsync("ACCOUNT…", limit: 50);           // mine.Items, mine.NextCursor
await foreach (var effect in indexer.EnumerateHistoryAsync("ACCOUNT…", TokenKind.Algo, 0, fromRound: 1_000_000))
{
    Console.WriteLine($"{effect.Round} {effect.Type} {effect.Delta} {effect.BalanceAfter}");
}
```

## Pagination and consistent reads

List responses carry `IndexedRound` (the round the data is as of) and `NextCursor` (opaque, pass it back as `cursor`; `null` on the last page). The `Enumerate…Async` helpers do this for you.
`EnumerateHoldersAsync` without `round` **pins the round of the first page** for all following pages, so the walk is consistent while the indexer advances. Pass `round` yourself to read a past state.

A past round of a very large token (for example the native token) can exceed the server read timeout (HTTP 503). Use `ExportHoldersAsync`, which runs an asynchronous scan on the server and streams the result:

```csharp
await foreach (var holder in indexer.ExportHoldersAsync(TokenKind.Algo, 0, round: 30_000_000, pollInterval: TimeSpan.FromSeconds(2)))
{
    // …
}
```

It throws `HolderScanException` if the scan fails or is cancelled; stopping early (`break`, an error, cancellation) cancels the scan on the server; and `AvmBalanceIndexerApiException` with status 429 when the server runs too many scans (retry later).

## Errors

```csharp
try
{
    await indexer.GetTokenAsync(TokenKind.Asa, 123);
}
catch (AvmBalanceIndexerApiException error)
{
    error.StatusCode;   // 404
    error.ApiMessage;   // message from the API
    error.IsNotFound;   // unknown token, round beyond the indexed one, or wrong chain
    error.IsRetryable;  // 429 / 503 / 504
    error.RetryAfter;   // TimeSpan? from Retry-After
}
catch (AvmBalanceIndexerConnectionException)
{
    // network failure or timeout
}
```

The SDK does **not** retry on its own; use `AddStandardResilienceHandler()` on the `HttpClient` (see above) or a loop that honours `RetryAfter`.
The public deployments limit each client address to 600 data requests per minute.

## Generated types

The response models (`StatusResponse`, `HolderDto`, `HoldersResponse`, `ScanResponse`, …) and the enums `ChainId` and `TokenKind` live in the namespace `AvmBalanceIndexer.Client.Generated`.
The generated HTTP client itself is internal.

## Development

```sh
dotnet test AvmBalanceIndexer.Client.slnx
dotnet pack src/AvmBalanceIndexer.Client -c Release
```

`src/AvmBalanceIndexer.Client/Generated/` is produced by `sdk/scripts/generate.sh` from `sdk/openapi/avm-balance-indexer.v1.json`; do not edit it. This folder builds on its own (it does not use the repository's `Directory.Build.props`).
See [sdk/README.md](../README.md) for regeneration, versioning and publishing.

## License

MIT

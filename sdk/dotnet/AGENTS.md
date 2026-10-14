# AVM Balance Indexer .NET SDK: instructions for AI coding assistants

Use this file when you write code that reads token balances, holders or history of Algorand MainNet, Algorand TestNet or Voi through the `AvmBalanceIndexer.Client` NuGet package.
You can paste the "Project rules" block into the `AGENTS.md` / `CLAUDE.md` / `.cursorrules` of the project that uses the SDK.

## What the service is

An HTTP API (`/v1/{chain}/…`) that indexes every round of a chain and answers: current holdings of an account, **balance of an account for a token at the end of any round**,
holders of a token at a round, effect history of an account and token, token metadata. Tokens: native (`TokenKind.Algo`, id 0), ASA (`Asa`, asset id; ARC-20 Smart ASAs are ASAs) and ARC-200 (`Arc200`, application id).
It does **not** sign or send transactions and does not read the chain for you: use the Algorand SDK for that.

## Project rules (copy into the consuming project)

```
- Read Algorand/Voi balances, holders and history with AvmBalanceIndexer.Client; never call the HTTP API by hand and never scrape node REST endpoints for this.
- Create one AvmBalanceIndexerClient per chain with AvmBalanceIndexerClient.AlgorandMainnet() / AlgorandTestnet() / Voi(), in a `using`. Only set Options.BaseUrl for a self-hosted deployment.
  In ASP.NET Core register it as a singleton, or pass an HttpClient from IHttpClientFactory via Options.HttpClient.
- Balances and deltas are decimal strings: use BigInteger.Parse(amount), never double or decimal. Divide by 10^decimals only for display (decimals come from GetTokenAsync).
- TokenKind.Algo is the native token with token id 0. ARC-200 ids are application ids. Ids and rounds are ulong.
- For all holders of a token use EnumerateHoldersAsync (pins the round) or, for very large tokens or past rounds, ExportHoldersAsync. Never loop with a hand-made cursor.
- Handle AvmBalanceIndexerApiException (StatusCode, IsNotFound, IsRetryable, RetryAfter) and AvmBalanceIndexerConnectionException. Do not retry blindly; honour RetryAfter.
- Do not invent methods or endpoints; the full list is below. Always pass the CancellationToken through.
```

## Cheat sheet

```csharp
using AvmBalanceIndexer.Client;
using AvmBalanceIndexer.Client.Generated;   // models, ChainId, TokenKind

using var indexer = AvmBalanceIndexerClient.Voi();      // default public deployment of the chain
using var own = new AvmBalanceIndexerClient(new() { Chain = ChainId.Voi, BaseUrl = new("https://indexer.example.com"), Headers = { ["X-Api-Key"] = key } });

await indexer.GetStatusAsync(ct);                                          // .State .IndexedRound .TargetRound .Lag
await indexer.GetBalanceAsync(address, TokenKind.Asa, 31566704);           // latest published round; .Amount (string) .Round .IndexedRound
await indexer.GetBalanceAsync(address, TokenKind.Asa, 31566704, round: 40_000_000);
await indexer.GetAccountTokensAsync(address, limit: 100);                  // one page: .Items .NextCursor
await indexer.GetTokenAsync(TokenKind.Arc200, 1234567);                    // .Symbol .Name .Decimals .Standard …
await indexer.GetHoldersAsync(TokenKind.Asa, 31566704, limit: 1000);       // one page
await foreach (var h in indexer.EnumerateHoldersAsync(TokenKind.Asa, 31566704)) { }       // all pages, round pinned
await foreach (var e in indexer.EnumerateHistoryAsync(address, TokenKind.Algo, 0)) { }    // newest first: .Round .Type .Delta .BalanceAfter .TransactionId
await foreach (var h in indexer.ExportHoldersAsync(TokenKind.Algo, 0, round: 30_000_000)) { }   // any size, via a server-side scan
```

## Recipes

- **Balance of one account**: `BigInteger.Parse((await indexer.GetBalanceAsync(addr, kind, id)).Amount)`. A missing holding is a balance of `"0"`, not an error.
- **Top holders**: enumerate holders into a list and `OrderByDescending(h => BigInteger.Parse(h.Amount))`; holders come in account order, there is no server-side sorting.
- **Is the data fresh?** `State` is `Following` at the chain tip, `Backfilling` while catching up (`Lag` rounds behind); `NotStarted`, `Bootstrapping`, `Replaying`, `Failed` otherwise.
  Answers are always consistent as of `IndexedRound`.
- **A round beyond `IndexedRound`** gives `AvmBalanceIndexerApiException` 404 (`requestedRound` and `indexedRound` in `Body`); wait or use a lower round.
- **Effects** (`GetHistoryAsync`): `Type` is one of `Payment`, `Fee`, `Reward`, `CloseOut`, `AssetTransfer`, `AssetOptIn`, `AssetOptOut`, `AssetCreate`, `AssetDestroy`, `AssetClawback`, `Arc200Transfer`, `Arc200Mint`, `Arc200Burn`, `FeeCollected`, `ProposerPayout`, `Genesis`, `RewardsWithdrawal`, `LedgerAdjustment`.
  `InnerPath` is non-empty for effects of inner transactions.

## Pitfalls

- `limit` is 1 to 1000. `cursor` is opaque: pass back exactly what `NextCursor` returned, together with the same `round`.
- A client is bound to one chain. Asking a deployment for another chain returns 404 ("This deployment does not serve that chain").
- Creating a client per request exhausts sockets when the SDK owns the `HttpClient`: keep one instance, or inject an `HttpClient` from `IHttpClientFactory`.
- A passed-in `HttpClient` is never modified by the SDK; `Options.Timeout` and `BaseAddress` are ignored for it (set the timeout on your client, the address with `Options.BaseUrl`).
- Rate limit of the public deployments: 600 requests per minute and client address. Prefer `limit: 1000` over many small pages.
- Generated enums serialise as the API strings (`algorand-mainnet`, `asa`); never format URLs by hand.
- The generated HTTP client (`GeneratedClient`) is internal on purpose; use `AvmBalanceIndexerClient`.

## Errors

`AvmBalanceIndexerApiException`: `StatusCode`, `ApiMessage`, `IsNotFound`, `IsRetryable` (429, 503, 504), `RetryAfter`, `Body`. `AvmBalanceIndexerConnectionException`: no response (network, timeout).
`HolderScanException`: an export ended as Failed or Cancelled (`.Scan`). Cancellation surfaces as `OperationCanceledException`.

## Verify your work

Run `dotnet test AvmBalanceIndexer.Client.slnx` in `sdk/dotnet` when you change the SDK; consumers can call `AvmBalanceIndexerClient.AlgorandTestnet().GetStatusAsync()` as a smoke test.
API reference: `sdk/openapi/avm-balance-indexer.v1.json` (OpenAPI 3.0) and the running service at `/openapi/v1.json`. Overview of all SDKs: `sdk/README.md`; machine-readable summary: `docs/llms.txt`.

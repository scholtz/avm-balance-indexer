# AVM Balance Indexer: TypeScript SDK

Typed client for the [AVM Balance Indexer](../../README.md): token balances, holders and history for **Algorand MainNet**, **Algorand TestNet** and **Voi**.
Native ALGO/VOI, ASAs (including ARC-20 Smart ASAs) and ARC-200 application tokens, at the end of any indexed round.

- Zero runtime dependencies (uses `fetch`), Node 20.3+, Deno and Bun, full type definitions. ESM only: from CommonJS use `await import("@scholtz/avm-balance-indexer")` (Node 22.12+ can also `require` it). Browsers need a proxy: the indexer sends no CORS headers (see "Choosing the deployment").
- Request and response types are **generated from the OpenAPI specification** of the service; a small hand-written layer adds defaults, pagination and errors.
- Works out of the box against the public deployments and against your own.

| Chain | `chain` | Default URL |
|---|---|---|
| Algorand MainNet | `Chains.AlgorandMainnet` | https://mainnet.balance-indexer.biatec.io |
| Algorand TestNet | `Chains.AlgorandTestnet` | https://testnet.balance-indexer.biatec.io |
| Voi | `Chains.Voi` | https://voi.balance-indexer.biatec.io |

## Install

```sh
npm install @scholtz/avm-balance-indexer
```

## Quick start

```ts
import { AvmBalanceIndexer, Chains } from "@scholtz/avm-balance-indexer";

const indexer = AvmBalanceIndexer.voi(); // or .algorandMainnet(), .algorandTestnet(), new AvmBalanceIndexer({ chain })

const status = await indexer.status();
console.log(status.state, status.indexedRound, status.targetRound); // "Following", 12345, 12346

// Balance of an account at the end of a round (omit `round` for the latest published one). Amounts are decimal strings.
const balance = await indexer.balance("ACCOUNT…", "asa", 31566704, { round: 40_000_000 });
console.log(BigInt(balance.amount));

// Every holder of a token, page by page.
for await (const holder of indexer.iterateHolders("asa", 31566704)) {
  console.log(holder.account, holder.amount);
}
```

A runnable version is in [`examples/quickstart.ts`](examples/quickstart.ts).

## Choosing the deployment

If you do not set a URL, the public deployment of the chain is used. Everything below is optional:

```ts
import { AvmBalanceIndexer, Chains } from "@scholtz/avm-balance-indexer";

const indexer = new AvmBalanceIndexer({
  chain: Chains.AlgorandMainnet,                 // which chain the deployment at baseUrl serves (default: algorand-mainnet)
  baseUrl: "https://indexer.example.com",        // your own deployment, a gateway, or a local port-forward (no /v1/... suffix)
  headers: { "X-Api-Key": process.env.KEY! },    // for example an API key of your own gateway
  timeoutMs: 15_000,                             // default 30 000, 0 disables
  fetch: myFetch,                                // proxies, tests, polyfills
});
```

The indexer does not send CORS headers, so a browser application cannot call a deployment of another origin directly: reach it through your own backend or reverse proxy and set `baseUrl` to that.

One client reads one chain. A deployment of the indexer serves exactly one chain, so use one client per chain:

```ts
const mainnet = AvmBalanceIndexer.algorandMainnet();
const voi = AvmBalanceIndexer.voi();
```

## Reading data

| Method | Returns |
|---|---|
| `status()` | ingestion state, indexed round, target round, lag |
| `accountTokens(address, { includeZero, cursor, limit })` | one page of the current holdings of an account |
| `balance(address, kind, tokenId, { round })` | balance at the end of a round (`amount` is a decimal string) |
| `history(address, kind, tokenId, { fromRound, toRound, cursor, limit })` | one page of balance effects, newest first |
| `holders(kind, tokenId, { round, includeZero, cursor, limit })` | one page of holders in account order |
| `token(kind, tokenId)` | symbol, name, decimals, standard, creator |
| `iterateAccountTokens` / `iterateHistory` / `iterateHolders` | async generators over all pages |
| `exportHolders(kind, tokenId, { round, includeZero })` | async generator over **every** holder of a token of any size (asynchronous scan) |
| `startHolderScan` / `holderScan` / `holderScanPage` / `cancelHolderScan` | the scan API behind `exportHolders` |

`kind` is `"algo"` (native, token id `0`), `"asa"` (asset id; ARC-20 Smart ASAs are ASAs) or `"arc200"` (application id).
Ids and rounds are numbers up to 2^53 - 1 (`bigint` and numeric strings are accepted within that range, larger values throw a `RangeError`); balances are **always decimal strings**, convert with `BigInt(amount)` and divide by `10 ** decimals` from `token()` for display.

```ts
const usdc = await indexer.token("asa", 31566704);                 // { symbol: "USDC", decimals: 6, standard: "Asa", … }
const mine = await indexer.accountTokens("ACCOUNT…", { limit: 50 }); // { items: [{ token, amount, optedIn, … }], nextCursor }
for await (const effect of indexer.iterateHistory("ACCOUNT…", "algo", 0, { fromRound: 1_000_000 })) {
  console.log(effect.round, effect.type, effect.delta, effect.balanceAfter);
}
```

## Pagination and consistent reads

A page size (`limit`) outside 1 to 1000 is refused before anything is sent (TypeScript `RangeError`, Python `ValueError`, .NET `ArgumentOutOfRangeException`, Go `ErrInvalidArgument`, Rust `Error::InvalidArgument`).

List responses carry `indexedRound` (the round the data is as of) and `nextCursor` (opaque, pass it back as `cursor`; `null` on the last page). The `iterate…`
helpers do this for you. `iterateHolders` without `round` **pins the round of the first page** for all following pages, so the walk is consistent while the
indexer advances. Pass `round` yourself to read a past state.

A past round of a very large token (for example the native token) can exceed the server read timeout (HTTP 503). Use `exportHolders`, which runs an asynchronous scan on the server
and streams the result:

```ts
for await (const holder of indexer.exportHolders("algo", 0, { round: 30_000_000, pollIntervalMs: 2000 })) {
  // …
}
```

It throws `ScanError` if the scan fails or is cancelled; stopping early (`break`, an error, an abort) cancels the scan on the server; and `ApiError` with status 429 when the server runs too many scans (retry later).

## Errors

```ts
import { ApiError, ConnectionError, DecodeError } from "@scholtz/avm-balance-indexer";

try {
  await indexer.token("asa", 123);
} catch (error) {
  if (error instanceof ApiError) {
    error.status;             // 404
    error.message;            // message from the API
    error.isNotFound;         // unknown token, round beyond the indexed one, or wrong chain
    error.isRetryable;        // 429 / 503 / 504
    error.retryAfterSeconds;  // from Retry-After
  } else if (error instanceof ConnectionError) {
    // network failure or timeout
  } else if (error instanceof DecodeError) {
    // a success answer that is not JSON of the documented shape (for example a proxy's HTML page)
  }
}
```

Every call accepts an `AbortSignal` (`signal` option); an aborted call rejects with the abort error, not with `ConnectionError`, while a timeout is a `ConnectionError`.

The SDK does **not** retry on its own; a retry loop that honours `retryAfterSeconds` is a few lines:

```ts
async function withRetry<T>(call: () => Promise<T>, attempts = 4): Promise<T> {
  for (let attempt = 1; ; attempt++) {
    try {
      return await call();
    } catch (error) {
      if (!(error instanceof ApiError) || !error.isRetryable || attempt === attempts) throw error;
      await new Promise((resolve) => setTimeout(resolve, (error.retryAfterSeconds ?? 2 ** attempt) * 1000));
    }
  }
}
```

The public deployments limit each client address to 600 data requests per minute.

## Using the generated types

All response types are exported (`StatusResponse`, `HolderDto`, `HoldersResponse`, `ScanResponse`, …). The raw generated API classes live in `src/generated` and are an implementation detail.

## Development

```sh
npm ci
npm run typecheck && npm test && npm run build
```

`src/generated/` is produced by `sdk/scripts/generate.sh` from `sdk/openapi/avm-balance-indexer.v1.json`; do not edit it. See [sdk/README.md](../README.md) for regeneration, versioning and publishing.

## License

MIT

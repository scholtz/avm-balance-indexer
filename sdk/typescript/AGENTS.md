# AVM Balance Indexer TypeScript SDK: instructions for AI coding assistants

Use this file when you write code that reads token balances, holders or history of Algorand MainNet, Algorand TestNet or Voi through `@scholtz/avm-balance-indexer`.
You can paste the "Project rules" block into the `AGENTS.md` / `CLAUDE.md` / `.cursorrules` of the project that uses the SDK.

## What the service is

An HTTP API (`/v1/{chain}/…`) that indexes every round of a chain and answers: current holdings of an account, **balance of an account for a token at the end of any round**,
holders of a token at a round, effect history of an account and token, token metadata. Tokens: native (`algo`, id 0), ASA (`asa`, asset id; ARC-20 Smart ASAs are ASAs) and ARC-200 (`arc200`, application id).
It does **not** sign or send transactions and does not read the chain for you: use `algosdk` for that.

## Project rules (copy into the consuming project)

```
- Read Algorand/Voi balances, holders and history with @scholtz/avm-balance-indexer; never call the HTTP API by hand and never scrape node REST endpoints for this.
- Create one client per chain: AvmBalanceIndexer.algorandMainnet() / .algorandTestnet() / .voi(). Only pass { baseUrl } for a self-hosted deployment.
- Balances and deltas are decimal strings: use BigInt(amount), never Number or parseFloat. Divide by 10n ** BigInt(decimals) only for display (decimals come from token()).
- kind is "algo" | "asa" | "arc200"; the native token is kind "algo", tokenId 0. ARC-200 ids are application ids.
- For all holders of a token use iterateHolders (pins the round) or, for very large tokens or past rounds, exportHolders. Never loop with a hand-made cursor.
- Handle ApiError (status, isNotFound, isRetryable, retryAfterSeconds) and ConnectionError. Do not retry blindly; honour retryAfterSeconds.
- Do not invent methods or endpoints; the full list is below.
```

## Cheat sheet

```ts
import { AvmBalanceIndexer, ApiError, Chains } from "@scholtz/avm-balance-indexer";

const indexer = AvmBalanceIndexer.voi();                       // default public deployment of the chain
const own = new AvmBalanceIndexer({ chain: Chains.Voi, baseUrl: "https://indexer.example.com", headers: { "X-Api-Key": key } });

await indexer.status();                                        // { state, indexedRound, targetRound, lag }
await indexer.balance(address, "asa", 31566704);               // latest published round; { amount: "123", round, indexedRound }
await indexer.balance(address, "asa", 31566704, { round: 40_000_000 });
await indexer.accountTokens(address, { includeZero: false, limit: 100 });   // one page: { items, nextCursor }
await indexer.token("arc200", 1234567);                        // { symbol, name, decimals, standard, … }
await indexer.holders("asa", 31566704, { limit: 1000 });       // one page
for await (const h of indexer.iterateHolders("asa", 31566704)) {}              // all pages, round pinned
for await (const e of indexer.iterateHistory(address, "algo", 0)) {}           // newest first: { round, type, delta, balanceAfter, transactionId }
for await (const h of indexer.exportHolders("algo", 0, { round: 30_000_000 })) {}   // any size, via a server-side scan
```

## Recipes

- **Balance of one account**: `BigInt((await indexer.balance(addr, kind, id)).amount)`. A missing holding is a balance of `"0"`, not an error.
- **Top holders**: iterate holders and sort by `BigInt(h.amount)` descending; there is no server-side sorting (holders come in account order).
- **Is the data fresh?** `status().state` is `Following` when the indexer is at the chain tip; `Backfilling` while it catches up (`lag` rounds behind); `NotStarted`, `Bootstrapping`, `Replaying`, `Failed` otherwise.
  Answers are always consistent as of `indexedRound`.
- **A round in the future** or beyond `indexedRound` gives `ApiError` 404 with `indexedRound` in the body; wait or use a lower round.
- **Effects** (`history`): `type` is one of `Payment`, `Fee`, `Reward`, `CloseOut`, `AssetTransfer`, `AssetOptIn`, `AssetOptOut`, `AssetCreate`, `AssetDestroy`, `AssetClawback`, `Arc200Transfer`, `Arc200Mint`, `Arc200Burn`, `FeeCollected`, `ProposerPayout`, `Genesis`, `RewardsWithdrawal`, `LedgerAdjustment`.
  `innerPath` is non-empty for effects of inner transactions.

## Pitfalls

- Numbers: ids and rounds are numbers up to 2^53 - 1 (`bigint` and numeric strings are accepted within that range). Larger values are rejected with a `RangeError` instead of being rounded; no chain reaches them.
- Cancellation: an aborted `AbortSignal` rejects with the abort error itself, not with `ConnectionError`; a timeout is a `ConnectionError`.
- `limit` is 1 to 1000. `cursor` is opaque: pass back exactly what `nextCursor` returned, together with the same `round`.
- A client is bound to one chain. Asking a deployment for another chain returns 404 ("This deployment does not serve that chain").
- Rate limit of the public deployments: 600 requests per minute and client address. Prefer `limit: 1000` over many small pages.
- Browser use: the indexer sends no CORS headers. Call it from a backend or through your own reverse proxy (set `baseUrl` to the proxy), never directly from page JavaScript.

## Errors

`ApiError`: `status`, `message`, `isNotFound`, `isRetryable` (429, 503, 504), `retryAfterSeconds`, `body`. `ConnectionError`: no response (network, timeout). `DecodeError`: a success answer that is not the documented JSON. `ScanError`: an export ended as Failed or Cancelled.
`RangeError` for a bad id or round, before any request is sent.

## Verify your work

Run `npm test` in this package when you change the SDK; consumers can run `await AvmBalanceIndexer.algorandTestnet().status()` as a smoke test.
API reference: `sdk/openapi/avm-balance-indexer.v1.json` (OpenAPI 3.0) and the running service at `/openapi/v1.json`. Overview of all SDKs: `sdk/README.md`; machine-readable summary: `docs/llms.txt`.

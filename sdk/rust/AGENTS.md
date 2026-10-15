# AVM Balance Indexer Rust SDK: instructions for AI coding assistants

Use this file when you write code that reads token balances, holders or history of Algorand MainNet, Algorand TestNet or Voi through the `avm-balance-indexer` crate.
You can paste the "Project rules" block into the `AGENTS.md` / `CLAUDE.md` / `.cursorrules` of the project that uses the SDK.

## What the service is

An HTTP API (`/v1/{chain}/…`) that indexes every round of a chain and answers: current holdings of an account, **balance of an account for a token at the end of any round**,
holders of a token at a round, effect history of an account and token, token metadata. Tokens: native (`TokenKind::Algo`, id 0), ASA (`Asa`, asset id; ARC-20 Smart ASAs are ASAs) and ARC-200 (`Arc200`, application id).
It does **not** sign or send transactions and does not read the chain for you: use an Algorand SDK for that.

## Project rules (copy into the consuming project)

```
- Read Algorand/Voi balances, holders and history with the avm-balance-indexer crate; never call the HTTP API by hand and never scrape node REST endpoints for this.
- Create one client per chain: AvmBalanceIndexer::new(ChainId::AlgorandMainnet | AlgorandTestnet | Voi)?. Only use AvmBalanceIndexer::builder(chain).base_url(..) for a self-hosted deployment.
  The client is Clone and cheap to share; create it once.
- Balances and deltas are decimal strings: parse into a big integer (num-bigint), never f64. Divide by 10^decimals only for display (decimals come from token()).
- TokenKind::Algo is the native token with token id 0. ARC-200 ids are application ids. Ids and rounds are u64; optional parameters are Option.
- For all holders of a token use holders_stream (pins the round) or, for very large tokens or past rounds, export_holders_stream. Never loop with a hand-made cursor.
- Handle avm_balance_indexer::Error (is_not_found, is_retryable, Error::Api { retry_after, .. }). Do not retry blindly; honour retry_after.
- Do not invent methods or endpoints; the full list is below. Everything is async and needs a tokio runtime.
```

## Cheat sheet

```rust
use avm_balance_indexer::{AccountTokensOptions, AvmBalanceIndexer, ChainId, ExportOptions, HistoryOptions, HoldersOptions, TokenKind};
use futures_util::TryStreamExt;

let indexer = AvmBalanceIndexer::new(ChainId::Voi)?;                         // default public deployment of the chain
let own = AvmBalanceIndexer::builder(ChainId::Voi).base_url("https://indexer.example.com").header("X-Api-Key", key).build()?;

indexer.status().await?;                                                     // .state .indexed_round (Option<u64>) .target_round .lag
indexer.balance(address, TokenKind::Asa, 31566704, None).await?;             // latest published round; .amount (String) .round .indexed_round
indexer.balance(address, TokenKind::Asa, 31566704, Some(40_000_000)).await?;
indexer.account_tokens(address, AccountTokensOptions::default()).await?;     // one page: .items .next_cursor
indexer.token(TokenKind::Arc200, 1234567).await?;                            // .symbol (Option) .name .decimals .standard …
indexer.holders(TokenKind::Asa, 31566704, HoldersOptions { limit: Some(1000), ..Default::default() }).await?;   // one page
let all: Vec<_> = indexer.holders_stream(TokenKind::Asa, 31566704, HoldersOptions::default()).try_collect().await?;   // all pages, round pinned
let mut history = std::pin::pin!(indexer.history_stream(address, TokenKind::Algo, 0, HistoryOptions::default()));      // newest first
let any_size = indexer.export_holders_stream(TokenKind::Algo, 0, ExportOptions { round: Some(30_000_000), ..Default::default() });   // a Stream, nothing is buffered
```

## Recipes

- **Balance of one account**: parse `indexer.balance(addr, kind, id, None).await?.amount` as a big integer. A missing holding is a balance of `"0"`, not an error.
- **Top holders**: collect `holders_stream` and sort by the parsed amount descending; holders come in account order, there is no server-side sorting.
- **Is the data fresh?** `status.state` is `Following` at the chain tip, `Backfilling` while catching up (`lag` rounds behind); `NotStarted`, `Bootstrapping`, `Replaying`, `Failed` otherwise.
  Answers are always consistent as of `indexed_round`.
- **A round beyond `indexed_round`** gives `Error::Api { status: 404, .. }` (the message says "The round is not indexed yet."); call `status()` for `indexed_round`, then wait or use a lower round.
- **Effects** (`history`): `type_` is one of `Payment`, `Fee`, `Reward`, `CloseOut`, `AssetTransfer`, `AssetOptIn`, `AssetOptOut`, `AssetCreate`, `AssetDestroy`, `AssetClawback`, `Arc200Transfer`, `Arc200Mint`, `Arc200Burn`, `FeeCollected`, `ProposerPayout`, `Genesis`, `RewardsWithdrawal`, `LedgerAdjustment`.
  `inner_path` is non-empty for effects of inner transactions.

## Pitfalls

- `limit` is 1 to 1000 (`u32` in the option structs; others return `Error::InvalidArgument`). `cursor` is opaque: pass back exactly what `next_cursor` returned, together with the same `round`.
- A client is bound to one chain. Asking a deployment for another chain returns 404 ("This deployment does not serve that chain").
- `Error::Api.body` holds the raw body only for statuses the API does not declare (for example a proxy's 502); for the declared errors (400, 404, 429, 503, 504) the message is the API's `error` text.
- Streams borrow the client (`+ '_`); pin them (`std::pin::pin!` or `Box::pin`) before calling `try_next`.
- Field names are snake_case and Rust keywords get a trailing underscore: `effect.type_`.
- Rate limit of the public deployments: 600 requests per minute and client address. Prefer `limit: Some(1000)` over many small pages.
- TLS is rustls; a custom `reqwest::Client` passed with `.http_client(..)` replaces the SDK's headers and timeout.

## Errors

`Error::Api { status, message, retry_after, body }`, `Error::Connection(String)` (no response: network, timeout), `Error::Decode(String)`, `Error::Scan(Box<ScanResponse>)` (an export ended as Failed or Cancelled), `Error::InvalidArgument(String)`.
Helpers: `error.is_not_found()`, `error.is_retryable()` (429, 503, 504). The HTML error page of a gateway for a declared status shows up as `Decode` without its status; behind such a gateway treat `Decode` like a transient failure yourself.

## Verify your work

Run `cargo test && cargo clippy --all-targets -- -D warnings` in this crate when you change the SDK; consumers can call `AvmBalanceIndexer::new(ChainId::AlgorandTestnet)?.status().await?` as a smoke test.
API reference: `sdk/openapi/avm-balance-indexer.v1.json` (OpenAPI 3.0) and the running service at `/openapi/v1.json`. Overview of all SDKs: `sdk/README.md`; machine-readable summary: `docs/llms.txt`.

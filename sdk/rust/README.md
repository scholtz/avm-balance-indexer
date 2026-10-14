# AVM Balance Indexer: Rust SDK

Typed async client for the [AVM Balance Indexer](../../README.md): token balances, holders and history for **Algorand MainNet**, **Algorand TestNet** and **Voi**.
Native ALGO/VOI, ASAs (including ARC-20 Smart ASAs) and ARC-200 application tokens, at the end of any indexed round.

- Async (`tokio` + `reqwest`, rustls), `futures` streams for pagination, `serde` models, Rust 1.89+.
- The HTTP client and the models are **generated from the OpenAPI specification** of the service ([progenitor](https://github.com/oxidecomputer/progenitor)); a small hand-written layer adds defaults, pagination and errors.
- Works out of the box against the public deployments and against your own.

| Chain | `ChainId` | Default URL |
|---|---|---|
| Algorand MainNet | `ChainId::AlgorandMainnet` | https://mainnet.balance-indexer.biatec.io |
| Algorand TestNet | `ChainId::AlgorandTestnet` | https://testnet.balance-indexer.biatec.io |
| Voi | `ChainId::Voi` | https://voi.balance-indexer.biatec.io |

## Install

```toml
[dependencies]
avm-balance-indexer = "1"
tokio = { version = "1", features = ["macros", "rt-multi-thread"] }
futures-util = "0.3"   # for the streams
```

## Quick start

```rust
use avm_balance_indexer::{AvmBalanceIndexer, ChainId, HoldersOptions, TokenKind};
use futures_util::TryStreamExt;

#[tokio::main]
async fn main() -> Result<(), avm_balance_indexer::Error> {
    let indexer = AvmBalanceIndexer::new(ChainId::Voi)?; // the public Voi deployment

    let status = indexer.status().await?;
    println!("{} {:?} {:?}", status.state, status.indexed_round, status.target_round); // Following Some(12345) Some(12346)

    // Balance of an account at the end of a round (None: the latest published one). Amounts are decimal strings.
    let balance = indexer.balance("ACCOUNT…", TokenKind::Asa, 31566704, Some(40_000_000)).await?;
    println!("{}", balance.amount);

    // Every holder of a token, page by page.
    let holders: Vec<_> = indexer.holders_stream(TokenKind::Asa, 31566704, HoldersOptions::default()).try_collect().await?;
    println!("{} holders", holders.len());
    Ok(())
}
```

A runnable version is in [`examples/quickstart.rs`](examples/quickstart.rs) (`cargo run --example quickstart`).

## Choosing the deployment

If you do not set a URL, the public deployment of the chain is used. Everything below is optional:

```rust
use std::time::Duration;

let indexer = AvmBalanceIndexer::builder(ChainId::AlgorandMainnet)   // which chain the deployment at base_url serves
    .base_url("https://indexer.example.com")     // your own deployment, a gateway, or a local port-forward (no /v1/... suffix)
    .header("X-Api-Key", key)                    // for example an API key of your own gateway
    .timeout(Duration::from_secs(15))            // default 30 s
    .build()?;
// .http_client(my_reqwest_client) replaces the default client (proxy, TLS settings); header and timeout then have no effect.
```

A deployment of the indexer serves exactly one chain, so use one client per chain. The client is cheap to `clone` and safe to share between tasks.

## Reading data

| Method | Returns |
|---|---|
| `status()` | ingestion state, indexed round, target round, lag |
| `account_tokens(address, AccountTokensOptions)` | one page of the current holdings of an account |
| `balance(address, kind, token_id, round: Option<u64>)` | balance at the end of a round (`amount` is a decimal string) |
| `history(address, kind, token_id, HistoryOptions)` | one page of balance effects, newest first |
| `holders(kind, token_id, HoldersOptions)` | one page of holders in account order |
| `token(kind, token_id)` | symbol, name, decimals, standard, creator |
| `account_tokens_stream` / `history_stream` / `holders_stream` | `Stream<Item = Result<…>>` over all pages |
| `export_holders_stream(kind, token_id, ExportOptions)` | stream over **every** holder of a token of any size (asynchronous scan) |
| `start_holder_scan` / `holder_scan` / `holder_scan_page` / `cancel_holder_scan` | the scan API behind `export_holders_stream` |

`TokenKind` is `Algo` (native, token id `0`), `Asa` (asset id; ARC-20 Smart ASAs are ASAs) or `Arc200` (application id). Ids and rounds are `u64`. Balances are **always decimal strings**:
parse with a big-integer crate (`num-bigint`) and divide by `10^decimals` from `token()` for display. Option structs implement `Default`, so `HoldersOptions { limit: Some(1000), ..Default::default() }` works.

```rust
let usdc = indexer.token(TokenKind::Asa, 31566704).await?;                       // symbol Some("USDC"), decimals Some(6)
let page = indexer.account_tokens("ACCOUNT…", AccountTokensOptions { limit: Some(50), ..Default::default() }).await?;
let mut effects = std::pin::pin!(indexer.history_stream("ACCOUNT…", TokenKind::Algo, 0, HistoryOptions::default()));
while let Some(effect) = effects.try_next().await? {                              // futures_util::TryStreamExt
    println!("{} {} {} {}", effect.round, effect.type_, effect.delta, effect.balance_after);
}
```

## Pagination and consistent reads

List responses carry `indexed_round` (the round the data is as of) and `next_cursor` (opaque, pass it back as `cursor`; `None` on the last page). The `…_stream` helpers do this for you.
`holders_stream` without `round` **pins the round of the first page** for all following pages, so the walk is consistent while the indexer advances. Set `round` yourself to read a past state.

A past round of a very large token (for example the native token) can exceed the server read timeout (HTTP 503). Use `export_holders_stream`, which runs an asynchronous scan on the server and yields the holders as the result pages become available:

```rust
let options = ExportOptions { round: Some(30_000_000), poll_interval: Some(Duration::from_secs(2)), ..Default::default() };
let mut holders = std::pin::pin!(indexer.export_holders_stream(TokenKind::Algo, 0, options));
while let Some(holder) = holders.try_next().await? {
    // …
}
```

The stream ends with `Error::Scan` if the scan fails or is cancelled. Dropping the stream before it ends cancels the scan on the server (best effort, from inside a Tokio runtime), and `Error::Api { status: 429, .. }` when the server runs too many scans (retry later).

## Errors

```rust
use avm_balance_indexer::Error;

match indexer.token(TokenKind::Asa, 123).await {
    Ok(token) => { /* … */ }
    Err(error) if error.is_not_found() => { /* unknown token, round beyond the indexed one, or wrong chain */ }
    Err(Error::Api { status, message, retry_after, .. }) => { /* retry_after: Option<Duration> from Retry-After */ }
    Err(Error::Connection(message)) => { /* network failure or timeout */ }
    Err(other) => return Err(other),
}
```

`error.is_retryable()` is true for 429, 503 and 504. A gateway's HTML error page for one of the declared statuses (404, 429, 503, 504) cannot be attributed to its status here and shows up as `Error::Decode`: if you sit behind such a gateway, treat `Decode` like a transient failure yourself. Invalid arguments (a `limit` outside 1 to 1000) return `Error::InvalidArgument` before any request is sent. An error answer that is not the API's JSON (for example an HTML page of a
proxy) is `Error::Api` with `body` for undeclared statuses and `Error::Decode` for the declared ones (400, 404, 429, 503, 504 without JSON).
The SDK does **not** retry on its own. The public deployments limit each client address to 600 data requests per minute.

## Generated code

`src/generated.rs` is private; the models (`StatusResponse`, `HolderDto`, `ScanResponse`, …) and the enums `ChainId` and `TokenKind` are re-exported at the crate root. Field names are snake_case
(`indexed_round`, `next_cursor`), Rust keywords get a trailing underscore (`type_`).

## Development

```sh
cargo test && cargo clippy --all-targets -- -D warnings
cargo package          # what is published
```

`src/generated.rs` is produced by `sdk/scripts/generate.sh` from `sdk/openapi/avm-balance-indexer.v1.json`; do not edit it. See [sdk/README.md](../README.md) for regeneration, versioning and publishing.

## License

MIT

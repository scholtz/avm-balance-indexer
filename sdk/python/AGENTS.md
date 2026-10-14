# AVM Balance Indexer Python SDK: instructions for AI coding assistants

Use this file when you write code that reads token balances, holders or history of Algorand MainNet, Algorand TestNet or Voi through the `avm-balance-indexer` package.
You can paste the "Project rules" block into the `AGENTS.md` / `CLAUDE.md` / `.cursorrules` of the project that uses the SDK.

## What the service is

An HTTP API (`/v1/{chain}/…`) that indexes every round of a chain and answers: current holdings of an account, **balance of an account for a token at the end of any round**,
holders of a token at a round, effect history of an account and token, token metadata. Tokens: native (`algo`, id 0), ASA (`asa`, asset id; ARC-20 Smart ASAs are ASAs) and ARC-200 (`arc200`, application id).
It does **not** sign or send transactions and does not read the chain for you: use `algosdk` for that.

## Project rules (copy into the consuming project)

```
- Read Algorand/Voi balances, holders and history with the avm-balance-indexer package; never call the HTTP API by hand and never scrape node REST endpoints for this.
- Create one client per chain: AvmBalanceIndexer.algorand_mainnet() / .algorand_testnet() / .voi(), preferably in a `with` block. Only pass base_url= for a self-hosted deployment.
- Balances and deltas are decimal strings: use int(amount), never float. Divide by 10 ** decimals only for display (decimals come from token()).
- kind is "algo" | "asa" | "arc200"; the native token is kind "algo", token_id 0. ARC-200 ids are application ids.
- For all holders of a token use iter_holders (pins the round) or, for very large tokens or past rounds, export_holders. Never loop with a hand-made cursor.
- Handle ApiError (status, is_not_found, is_retryable, retry_after_seconds) and IndexerConnectionError. Do not retry blindly; honour retry_after_seconds.
- Do not invent methods or endpoints; the full list is below. Attributes are snake_case (indexed_round, next_cursor).
```

## Cheat sheet

```python
from avm_balance_indexer import ApiError, AvmBalanceIndexer, VOI

with AvmBalanceIndexer.voi() as indexer:                       # default public deployment of the chain
    own = AvmBalanceIndexer(chain=VOI, base_url="https://indexer.example.com", headers={"X-Api-Key": key})

    indexer.status()                                           # .state .indexed_round .target_round .lag
    indexer.balance(address, "asa", 31566704)                  # latest published round; .amount (str) .round .indexed_round
    indexer.balance(address, "asa", 31566704, round=40_000_000)
    indexer.account_tokens(address, limit=100)                 # one page: .items .next_cursor
    indexer.token("arc200", 1234567)                           # .symbol .name .decimals .standard …
    indexer.holders("asa", 31566704, limit=1000)               # one page
    for h in indexer.iter_holders("asa", 31566704): ...        # all pages, round pinned
    for e in indexer.iter_history(address, "algo", 0): ...     # newest first: .round .type .delta .balance_after .transaction_id
    for h in indexer.export_holders("algo", 0, round=30_000_000): ...   # any size, via a server-side scan
```

## Recipes

- **Balance of one account**: `int(indexer.balance(addr, kind, id).amount)`. A missing holding is a balance of `"0"`, not an error.
- **Top holders**: `sorted(indexer.iter_holders(kind, id), key=lambda h: int(h.amount), reverse=True)[:10]`; holders come in account order, there is no server-side sorting.
- **Is the data fresh?** `status().state` is `Following` at the chain tip, `Backfilling` while catching up (`lag` rounds behind); `NotStarted`, `Bootstrapping`, `Replaying`, `Failed` otherwise.
  Answers are always consistent as of `indexed_round`.
- **A round beyond `indexed_round`** gives `ApiError` 404 (`requestedRound` and `indexedRound` in `error.body`); wait or use a lower round.
- **Effects** (`history`): `type` is one of `Payment`, `Fee`, `Reward`, `CloseOut`, `AssetTransfer`, `AssetOptIn`, `AssetOptOut`, `AssetCreate`, `AssetDestroy`, `AssetClawback`, `Arc200Transfer`, `Arc200Mint`, `Arc200Burn`, `FeeCollected`, `ProposerPayout`, `Genesis`, `RewardsWithdrawal`, `LedgerAdjustment`.
  `inner_path` is non-empty for effects of inner transactions.

## Pitfalls

- `limit` is 1 to 1000. `cursor` is opaque: pass back exactly what `next_cursor` returned, together with the same `round`.
- A client is bound to one chain. Asking a deployment for another chain returns 404 ("This deployment does not serve that chain").
- `IndexerConnectionError` shadows the builtin only if you import it with `from avm_balance_indexer import IndexerConnectionError`; prefer `import avm_balance_indexer as avm` and `avm.IndexerConnectionError`.
- Rate limit of the public deployments: 600 requests per minute and client address. Prefer `limit=1000` over many small pages.
- The client is synchronous (urllib3). In asyncio code run it with `asyncio.to_thread(...)`.
- Rounds and ids are Python ints (no overflow); pass them as ints or numeric strings.

## Errors

`ApiError`: `status`, `message`, `is_not_found`, `is_retryable` (429, 503, 504), `retry_after_seconds`, `body`. `IndexerConnectionError`: no response (network, timeout). `ResponseDecodeError`: a success answer that is not the documented JSON. `ScanError`: an export ended as Failed or Cancelled (`.scan`).
`ValueError` for a bad kind, id or round, before any request is sent.

## Verify your work

Run `pytest` in this package when you change the SDK; consumers can run `AvmBalanceIndexer.algorand_testnet().status()` as a smoke test.
API reference: `sdk/openapi/avm-balance-indexer.v1.json` (OpenAPI 3.0) and the running service at `/openapi/v1.json`. Overview of all SDKs: `sdk/README.md`; machine-readable summary: `docs/llms.txt`.

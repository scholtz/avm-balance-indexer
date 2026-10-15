# AVM Balance Indexer: Python SDK

Typed client for the [AVM Balance Indexer](../../README.md): token balances, holders and history for **Algorand MainNet**, **Algorand TestNet** and **Voi**.
Native ALGO/VOI, ASAs (including ARC-20 Smart ASAs) and ARC-200 application tokens, at the end of any indexed round.

- Python 3.9+, [pydantic](https://docs.pydantic.dev) v2 models, fully type-annotated (`py.typed`).
- Request and response models are **generated from the OpenAPI specification** of the service; a small hand-written layer adds defaults, pagination and errors.
- Works out of the box against the public deployments and against your own.

| Chain | `chain` | Default URL |
|---|---|---|
| Algorand MainNet | `ALGORAND_MAINNET` (`"algorand-mainnet"`) | https://mainnet.balance-indexer.biatec.io |
| Algorand TestNet | `ALGORAND_TESTNET` (`"algorand-testnet"`) | https://testnet.balance-indexer.biatec.io |
| Voi | `VOI` (`"voi"`) | https://voi.balance-indexer.biatec.io |

## Install

```sh
pip install avm-balance-indexer
```

## Quick start

```python
from avm_balance_indexer import AvmBalanceIndexer

with AvmBalanceIndexer.voi() as indexer:  # or .algorand_mainnet(), .algorand_testnet(), AvmBalanceIndexer(chain=...)
    status = indexer.status()
    print(status.state, status.indexed_round, status.target_round)   # Following 12345 12346

    # Balance of an account at the end of a round (omit `round` for the latest published one). Amounts are decimal strings.
    balance = indexer.balance("ACCOUNT…", "asa", 31566704, round=40_000_000)
    print(int(balance.amount))

    # Every holder of a token, page by page.
    for holder in indexer.iter_holders("asa", 31566704):
        print(holder.account, holder.amount)
```

A runnable version is in [`examples/quickstart.py`](examples/quickstart.py).

## Choosing the deployment

If you do not set a URL, the public deployment of the chain is used. Everything below is optional:

```python
from avm_balance_indexer import ALGORAND_MAINNET, AvmBalanceIndexer

indexer = AvmBalanceIndexer(
    chain=ALGORAND_MAINNET,                      # which chain the deployment at base_url serves (default: algorand-mainnet)
    base_url="https://indexer.example.com",      # your own deployment, a gateway, or a local port-forward (no /v1/... suffix)
    headers={"X-Api-Key": os.environ["KEY"]},    # for example an API key of your own gateway
    timeout=15.0,                                # seconds per request, default 30, None disables
)
```

A deployment of the indexer serves exactly one chain, so use one client per chain. The client keeps a connection pool; use it as a context manager or call `close()`.

## Reading data

| Method | Returns |
|---|---|
| `status()` | ingestion state, indexed round, target round, lag |
| `account_tokens(address, include_zero, cursor, limit)` | one page of the current holdings of an account |
| `balance(address, kind, token_id, round=None)` | balance at the end of a round (`amount` is a decimal string) |
| `history(address, kind, token_id, from_round, to_round, cursor, limit)` | one page of balance effects, newest first |
| `holders(kind, token_id, round, include_zero, cursor, limit)` | one page of holders in account order |
| `token(kind, token_id)` | symbol, name, decimals, standard, creator |
| `iter_account_tokens` / `iter_history` / `iter_holders` | generators over all pages |
| `export_holders(kind, token_id, round, include_zero)` | generator over **every** holder of a token of any size (asynchronous scan) |
| `start_holder_scan` / `holder_scan` / `holder_scan_page` / `cancel_holder_scan` | the scan API behind `export_holders` |

`kind` is `"algo"` (native, token id `0`), `"asa"` (asset id; ARC-20 Smart ASAs are ASAs) or `"arc200"` (application id).
Models use snake_case attributes (`indexed_round`, `next_cursor`, `last_changed_round`, …). Balances are **always decimal strings**: convert with `int(amount)` and divide by `10 ** decimals` from `token()` for display.

```python
usdc = indexer.token("asa", 31566704)                          # symbol="USDC", decimals=6, standard="Asa", …
page = indexer.account_tokens("ACCOUNT…", limit=50)            # page.items, page.next_cursor
for effect in indexer.iter_history("ACCOUNT…", "algo", 0, from_round=1_000_000):
    print(effect.round, effect.type, effect.delta, effect.balance_after)
```

## Pagination and consistent reads

A page size (`limit`) outside 1 to 1000 is refused before anything is sent (TypeScript `RangeError`, Python `ValueError`, .NET `ArgumentOutOfRangeException`, Go `ErrInvalidArgument`, Rust `Error::InvalidArgument`).

List responses carry `indexed_round` (the round the data is as of) and `next_cursor` (opaque, pass it back as `cursor`; `None` on the last page). The `iter_…` helpers do this for you.
`iter_holders` without `round` **pins the round of the first page** for all following pages, so the walk is consistent while the indexer advances. Pass `round` yourself to read a past state.

A past round of a very large token (for example the native token) can exceed the server read timeout (HTTP 503). Use `export_holders`, which runs an asynchronous scan on the server and streams the result:

```python
for holder in indexer.export_holders("algo", 0, round=30_000_000, poll_interval=2.0):
    ...
```

It raises `ScanError` if the scan fails or is cancelled; stopping early (`break`, an error) cancels the scan on the server; and `ApiError` with status 429 when the server runs too many scans (retry later).

## Errors

```python
from avm_balance_indexer import ApiError, IndexerConnectionError, ResponseDecodeError, ScanError

try:
    indexer.token("asa", 123)
except ApiError as error:
    error.status               # 404
    error.message              # message from the API
    error.is_not_found         # unknown token, round beyond the indexed one, or wrong chain
    error.is_retryable         # 429 / 503 / 504
    error.retry_after_seconds  # from Retry-After
except IndexerConnectionError:
    ...                        # network failure or timeout
```

`IndexerConnectionError` is also a subclass of the builtin `ConnectionError`; `ResponseDecodeError` means a success answer that is not the documented JSON. Invalid arguments (unknown `kind`, negative ids) raise `ValueError` before any request is sent.

The SDK does **not** retry on its own; a retry loop that honours `retry_after_seconds`:

```python
import time

def with_retry(call, attempts=4):
    for attempt in range(1, attempts + 1):
        try:
            return call()
        except ApiError as error:
            if not error.is_retryable or attempt == attempts:
                raise
            time.sleep(error.retry_after_seconds or 2 ** attempt)
```

The public deployments limit each client address to 600 data requests per minute.

Requests honour the `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY` and `NO_PROXY` environment variables (and the operating system proxy settings), as urllib3 and requests do; add a local indexer to `NO_PROXY` when a proxy is configured.

## Development

```sh
python -m venv .venv && . .venv/bin/activate        # Windows: .venv\Scripts\activate
pip install -e ".[dev]"
pytest
python -m build                                     # sdist and wheel
```

`src/avm_balance_indexer/_generated/` is produced by `sdk/scripts/generate.sh` from `sdk/openapi/avm-balance-indexer.v1.json`; do not edit it. See [sdk/README.md](../README.md) for regeneration, versioning and publishing.

## License

MIT

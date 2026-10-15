"""Hand-written client on top of the generated code in ``_generated``."""

from __future__ import annotations

import builtins
import json
import time
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from urllib.parse import urlparse
from typing import Dict, Iterator, Mapping, Optional, Union

import urllib3

from ._generated import ApiClient, Configuration
from ._generated.api.balances_api import BalancesApi
from ._generated.api.history_api import HistoryApi
from ._generated.api.holders_api import HoldersApi
from ._generated.api.scans_api import ScansApi
from ._generated.api.status_api import StatusApi
from ._generated.api.tokens_api import TokensApi
from ._generated.exceptions import ApiException
from ._generated.models import (
    BalanceResponse,
    EffectDto,
    HistoryResponse,
    HolderDto,
    HoldersResponse,
    HoldingDto,
    HoldingsResponse,
    ScanHoldersResponse,
    ScanRequest,
    ScanResponse,
    StatusResponse,
    TokenResponse,
)

__all__ = [
    "ALGORAND_MAINNET",
    "ALGORAND_TESTNET",
    "VOI",
    "CHAINS",
    "DEFAULT_BASE_URLS",
    "TOKEN_KINDS",
    "AvmBalanceIndexer",
    "ApiError",
    "IndexerConnectionError",
    "ResponseDecodeError",
    "ScanError",
]

ALGORAND_MAINNET = "algorand-mainnet"
ALGORAND_TESTNET = "algorand-testnet"
VOI = "voi"

#: The chains an indexer deployment can serve.
CHAINS = (ALGORAND_MAINNET, ALGORAND_TESTNET, VOI)

#: Public deployments, used when no ``base_url`` is given.
DEFAULT_BASE_URLS: Mapping[str, str] = {
    ALGORAND_MAINNET: "https://mainnet.balance-indexer.biatec.io",
    ALGORAND_TESTNET: "https://testnet.balance-indexer.biatec.io",
    VOI: "https://voi.balance-indexer.biatec.io",
}

#: ``algo`` (native, token id 0), ``asa`` (Algorand Standard Asset, also ARC-20 Smart ASAs), ``arc200`` (application token).
TOKEN_KINDS = ("algo", "asa", "arc200")

_TERMINAL_SCAN_STATES = {"Completed", "Failed", "Cancelled"}

Id = Union[int, str]


class ApiError(Exception):
    """The server answered with an error status."""

    def __init__(self, status: int, message: str, retry_after_seconds: Optional[float] = None, body: Optional[str] = None) -> None:
        super().__init__(f"HTTP {status}: {message}")
        self.status = status
        self.message = message
        #: Seconds to wait before retrying, from ``Retry-After`` (429 and 503).
        self.retry_after_seconds = retry_after_seconds
        self.body = body

    @property
    def is_not_found(self) -> bool:
        """Unknown token, round beyond the indexed one, or wrong chain."""
        return self.status == 404

    @property
    def is_retryable(self) -> bool:
        """Rate limited or nothing indexed yet: retry after ``retry_after_seconds``."""
        return self.status in (429, 503, 504)


class IndexerConnectionError(builtins.ConnectionError):
    """The request never produced a response (network failure or timeout). Also a ``ConnectionError`` of the standard library."""


class ResponseDecodeError(Exception):
    """The server answered with a success status but the body is not what the API documents (for example an HTML page of a proxy)."""


class ScanError(Exception):
    """A holder scan ended as ``Failed`` or ``Cancelled`` before it finished."""

    def __init__(self, message: str, scan: ScanResponse) -> None:
        super().__init__(message)
        self.scan = scan


def _id(value: Id, what: str) -> int:
    # Only ints and strings of digits: int(1.9) would silently become 1 and True would become 1.
    if isinstance(value, bool) or not (isinstance(value, int) or (isinstance(value, str) and value.isascii() and value.isdigit())):
        raise ValueError(f"{what} must be a non-negative integer (got {value!r})")
    number = int(value)
    if number < 0 or number >= 2**64:
        raise ValueError(f"{what} must be a non-negative 64-bit integer (got {value!r})")
    return number


def _limit(limit: Optional[int]) -> Optional[int]:
    if limit is not None and (isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 1000):
        raise ValueError(f"limit must be an integer from 1 to 1000 (got {limit!r})")
    return limit


def _kind(kind: str) -> str:
    if kind not in TOKEN_KINDS:
        raise ValueError(f"kind must be one of {', '.join(TOKEN_KINDS)} (got {kind!r})")
    return kind


class AvmBalanceIndexer:
    """Client for one chain of an AVM Balance Indexer deployment.

    >>> indexer = AvmBalanceIndexer(chain=VOI)  # public Voi deployment
    >>> custom = AvmBalanceIndexer(chain=ALGORAND_MAINNET, base_url="https://indexer.example.com")

    :param chain: which chain the indexer at ``base_url`` serves (default ``algorand-mainnet``).
    :param base_url: root URL of a deployment, without ``/v1/...`` (default: the public deployment of ``chain``).
    :param headers: extra headers for every request.
    :param timeout: seconds per request (default 30; ``None`` disables it).
    """

    def __init__(
        self,
        chain: str = ALGORAND_MAINNET,
        base_url: Optional[str] = None,
        headers: Optional[Mapping[str, str]] = None,
        timeout: Optional[float] = 30.0,
    ) -> None:
        if chain not in DEFAULT_BASE_URLS:
            raise ValueError(f"Unknown chain {chain!r}; use one of {', '.join(CHAINS)}")
        self.chain = chain
        self.base_url = (base_url or DEFAULT_BASE_URLS[chain]).rstrip("/")
        parsed = urlparse(self.base_url)
        if parsed.scheme not in ("http", "https") or not parsed.netloc:
            raise ValueError(f"base_url must be an absolute http or https URL (got {self.base_url!r})")
        self.timeout = timeout

        configuration = Configuration(host=self.base_url)
        # No hidden retries: errors surface at once (ApiError.retry_after_seconds tells when to retry).
        configuration.retries = urllib3.Retry(total=None, connect=0, read=0, status=0, other=0, redirect=3)
        self._api_client = ApiClient(configuration)
        self._api_client.user_agent = "avm-balance-indexer-python-sdk"
        for name, value in (headers or {}).items():
            self._api_client.set_default_header(name, value)
        self._status = StatusApi(self._api_client)
        self._balances = BalancesApi(self._api_client)
        self._history = HistoryApi(self._api_client)
        self._holders = HoldersApi(self._api_client)
        self._tokens = TokensApi(self._api_client)
        self._scans = ScansApi(self._api_client)

    # -- construction helpers -------------------------------------------------------------------

    @classmethod
    def algorand_mainnet(cls, **kwargs) -> "AvmBalanceIndexer":
        """Public MainNet deployment (or ``base_url``)."""
        return cls(chain=ALGORAND_MAINNET, **kwargs)

    @classmethod
    def algorand_testnet(cls, **kwargs) -> "AvmBalanceIndexer":
        """Public TestNet deployment (or ``base_url``)."""
        return cls(chain=ALGORAND_TESTNET, **kwargs)

    @classmethod
    def voi(cls, **kwargs) -> "AvmBalanceIndexer":
        """Public Voi deployment (or ``base_url``)."""
        return cls(chain=VOI, **kwargs)

    def close(self) -> None:
        """Releases the connection pool."""
        self._api_client.rest_client.pool_manager.clear()

    def __enter__(self) -> "AvmBalanceIndexer":
        return self

    def __exit__(self, *exc_info) -> None:
        self.close()

    @staticmethod
    def _check_arguments(args, kwargs) -> None:
        """What the generated methods would reject with a pydantic error, reported before anything is sent."""
        for value in args:
            if isinstance(value, bool) or not isinstance(value, (str, int)):
                raise ValueError(f"Invalid argument {value!r}: expected a string or an integer")
        for name in ("cursor",):
            if kwargs.get(name) is not None and not isinstance(kwargs[name], str):
                raise ValueError(f"{name} must be a string (got {kwargs[name]!r})")
        if kwargs.get("include_zero") is not None and not isinstance(kwargs["include_zero"], bool):
            raise ValueError(f"include_zero must be a boolean (got {kwargs['include_zero']!r})")

    def _call(self, function, *args, **kwargs):
        self._check_arguments(args, kwargs)
        try:
            return function(*args, _request_timeout=self.timeout, **kwargs)
        except ApiException as error:
            if _is_transport_failure(error):
                raise IndexerConnectionError(f"Request to {self.base_url} failed: {error.reason or error}") from error
            raise _to_api_error(error) from error
        except urllib3.exceptions.HTTPError as error:
            raise IndexerConnectionError(f"Request to {self.base_url} failed: {error}") from error
        except ValueError as error:  # pydantic's ValidationError and JSON errors are ValueErrors
            # The generated methods validate their own arguments first; that error is titled with the method (BalancesApi.get_account_tokens),
            # a response that does not fit a model with the model (HoldingsResponse).
            if getattr(error, "title", None) == getattr(function, "__qualname__", None):
                raise ValueError(f"Invalid argument: {error}") from error
            raise ResponseDecodeError(f"Unexpected response from {self.base_url}: {error}") from error

    # -- single requests ------------------------------------------------------------------------

    def status(self) -> StatusResponse:
        """Indexed round, target round, lag and ingestion state."""
        return self._call(self._status.get_chain_status, self.chain)

    def account_tokens(self, address: str, include_zero: Optional[bool] = None, cursor: Optional[str] = None, limit: Optional[int] = None) -> HoldingsResponse:
        """Current holdings of an account (one page)."""
        return self._call(self._balances.get_account_tokens, self.chain, address, include_zero=include_zero, cursor=cursor, limit=_limit(limit))

    def balance(self, address: str, kind: str, token_id: Id, round: Optional[Id] = None) -> BalanceResponse:  # noqa: A002
        """End-of-round balance of an account and token. ``amount`` is a decimal string."""
        return self._call(
            self._balances.get_token_balance,
            self.chain,
            address,
            _kind(kind),
            _id(token_id, "token_id"),
            round=None if round is None else _id(round, "round"),
        )

    def history(
        self,
        address: str,
        kind: str,
        token_id: Id,
        from_round: Optional[Id] = None,
        to_round: Optional[Id] = None,
        cursor: Optional[str] = None,
        limit: Optional[int] = None,
    ) -> HistoryResponse:
        """Balance effects of an account and token, newest first (one page)."""
        return self._call(
            self._history.get_token_history,
            self.chain,
            address,
            _kind(kind),
            _id(token_id, "token_id"),
            from_round=None if from_round is None else _id(from_round, "from_round"),
            to_round=None if to_round is None else _id(to_round, "to_round"),
            cursor=cursor,
            limit=_limit(limit),
        )

    def holders(
        self,
        kind: str,
        token_id: Id,
        round: Optional[Id] = None,  # noqa: A002
        include_zero: Optional[bool] = None,
        cursor: Optional[str] = None,
        limit: Optional[int] = None,
    ) -> HoldersResponse:
        """Holders of a token at the end of a round (one page, in account order)."""
        return self._call(
            self._holders.get_token_holders,
            self.chain,
            _kind(kind),
            _id(token_id, "token_id"),
            round=None if round is None else _id(round, "round"),
            include_zero=include_zero,
            cursor=cursor,
            limit=_limit(limit),
        )

    def token(self, kind: str, token_id: Id) -> TokenResponse:
        """Token metadata (symbol, name, decimals, standard)."""
        return self._call(self._tokens.get_token, self.chain, _kind(kind), _id(token_id, "token_id"))

    def start_holder_scan(self, kind: str, token_id: Id, round: Optional[Id] = None, include_zero: Optional[bool] = None) -> ScanResponse:  # noqa: A002
        """Starts an asynchronous export of every holder of a token. Prefer :meth:`export_holders`."""
        request = ScanRequest(round=None if round is None else _id(round, "round"), includeZero=include_zero)
        return self._call(self._scans.create_holder_scan, self.chain, _kind(kind), _id(token_id, "token_id"), scan_request=request)

    def holder_scan(self, scan_id: str) -> ScanResponse:
        """State and progress of a scan."""
        return self._call(self._scans.get_holder_scan, self.chain, scan_id)

    def holder_scan_page(self, scan_id: str, cursor: Optional[str] = None, limit: Optional[int] = None) -> ScanHoldersResponse:
        """A result page of a scan (readable while it runs)."""
        return self._call(self._scans.get_holder_scan_result, self.chain, scan_id, cursor=cursor, limit=_limit(limit))

    def cancel_holder_scan(self, scan_id: str) -> ScanResponse:
        """Cancels a scan."""
        return self._call(self._scans.cancel_holder_scan, self.chain, scan_id)

    # -- iteration ------------------------------------------------------------------------------

    def iter_account_tokens(self, address: str, include_zero: Optional[bool] = None, limit: Optional[int] = None) -> Iterator[HoldingDto]:
        """Every holding of an account, page by page."""
        cursor: Optional[str] = None
        while True:
            page = self.account_tokens(address, include_zero=include_zero, cursor=cursor, limit=_limit(limit))
            yield from page.items
            cursor = page.next_cursor
            if not cursor:
                return

    def iter_history(
        self,
        address: str,
        kind: str,
        token_id: Id,
        from_round: Optional[Id] = None,
        to_round: Optional[Id] = None,
        limit: Optional[int] = None,
    ) -> Iterator[EffectDto]:
        """Every effect of an account and token, newest first."""
        cursor: Optional[str] = None
        while True:
            page = self.history(address, kind, token_id, from_round=from_round, to_round=to_round, cursor=cursor, limit=_limit(limit))
            yield from page.items
            cursor = page.next_cursor
            if not cursor:
                return

    def iter_holders(
        self,
        kind: str,
        token_id: Id,
        round: Optional[Id] = None,  # noqa: A002
        include_zero: Optional[bool] = None,
        limit: Optional[int] = None,
    ) -> Iterator[HolderDto]:
        """Every holder of a token at one round.

        Without ``round`` the round of the first page is pinned for the following pages, so the walk stays consistent while the
        indexer advances. A very large token at a past round can time out (HTTP 503): use :meth:`export_holders` for those.
        """
        cursor: Optional[str] = None
        while True:
            page = self.holders(kind, token_id, round=round, include_zero=include_zero, cursor=cursor, limit=_limit(limit))
            if round is None:
                round = page.round
            yield from page.items
            cursor = page.next_cursor
            if not cursor:
                return

    def export_holders(
        self,
        kind: str,
        token_id: Id,
        round: Optional[Id] = None,  # noqa: A002
        include_zero: Optional[bool] = None,
        poll_interval: float = 1.0,
        limit: Optional[int] = None,
    ) -> Iterator[HolderDto]:
        """Exports every holder of a token through an asynchronous scan: starts it, waits for it and streams the result.

        Works for tokens of any size. Raises :class:`ScanError` if the scan fails or is cancelled. Stopping early (``break``, ``close()``, an error)
        cancels the scan on the server.
        """
        _limit(limit)  # before the scan exists
        if isinstance(poll_interval, bool) or not isinstance(poll_interval, (int, float)) or not 0 < poll_interval < float("inf"):
            raise ValueError(f"poll_interval must be a positive number of seconds (got {poll_interval!r})")
        started = self.start_holder_scan(kind, token_id, round=round, include_zero=include_zero)
        scan_id = str(started.id)
        cursor: Optional[str] = None
        finished = False
        try:
            while True:
                page = self.holder_scan_page(scan_id, cursor=cursor, limit=_limit(limit))
                # A finished scan has nothing left to release, whatever the consumer does with the last rows.
                finished = page.complete
                yield from page.items
                if page.complete:
                    return
                if page.next_cursor:
                    cursor = page.next_cursor
                    if page.items:
                        continue  # more rows may be waiting right now
                # Caught up with a running scan (an empty page), or no row exists yet: look at the scan, then wait. The cursor is kept.
                scan = self.holder_scan(scan_id)
                if scan.state in _TERMINAL_SCAN_STATES and scan.state != "Completed":
                    finished = True
                    detail = f": {scan.error}" if scan.error else ""
                    raise ScanError(f"Holder scan {scan_id} ended as {scan.state}{detail}", scan)
                if scan.state == "Completed" and page.items and not page.next_cursor:
                    # The scan finished between two reads of the server: this was its last page, every row has been delivered.
                    finished = True
                    return
                time.sleep(poll_interval)
        finally:
            if not finished:
                # Stopped early (break, close(), error): release the server-side scan, which counts against the server's limit of active scans.
                try:
                    # A short timeout of its own: the cleanup must not delay the error that ended the export.
                    self._scans.cancel_holder_scan(self.chain, scan_id, _request_timeout=5.0)
                except Exception:  # noqa: BLE001 - best effort
                    pass


def _to_api_error(error: ApiException) -> ApiError:
    message = error.reason or f"HTTP {error.status}"
    body = error.body
    if body:
        try:
            parsed = json.loads(body)
            if isinstance(parsed, dict) and isinstance(parsed.get("error"), str):
                message = parsed["error"]
        except ValueError:
            pass
    retry_after: Optional[float] = None
    headers: Optional[Mapping[str, str]] = error.headers  # usually a case-insensitive urllib3 HTTPHeaderDict, not guaranteed
    if headers:
        by_name = {str(name).lower(): value for name, value in headers.items()}
        retry_after = _parse_retry_after(by_name.get("retry-after"))
    return ApiError(error.status or 0, message, retry_after, body)


def _is_transport_failure(error: ApiException) -> bool:
    """The generated client reports a TLS failure or an unusable connection as status 0 (or none)."""
    return not error.status


def _parse_retry_after(raw: Optional[str]) -> Optional[float]:
    """``Retry-After`` is a number of seconds or an HTTP date; anything else (or an absurd value) is ignored."""
    if not raw:
        return None
    try:
        seconds = float(raw)
    except ValueError:
        try:
            seconds = (parsedate_to_datetime(raw) - datetime.now(timezone.utc)).total_seconds()
        except (TypeError, ValueError):
            return None
    return seconds if 0 < seconds < 1e9 else None

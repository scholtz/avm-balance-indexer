import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import parse_qs, urlparse

import pytest

from avm_balance_indexer import (
    ALGORAND_MAINNET,
    ALGORAND_TESTNET,
    CHAINS,
    DEFAULT_BASE_URLS,
    VOI,
    ApiError,
    AvmBalanceIndexer,
    IndexerConnectionError,
    ResponseDecodeError,
    ScanError,
)


class Server:
    """A tiny HTTP server whose answers the test sets; it records every request."""

    def __init__(self):
        self.requests = []
        self.handler = lambda request: (500, {"error": "unexpected"}, {})
        outer = self

        class Handler(BaseHTTPRequestHandler):
            def _serve(self):
                length = int(self.headers.get("Content-Length") or 0)
                body = self.rfile.read(length).decode() if length else ""
                request = {"method": self.command, "path": self.path, "headers": dict(self.headers), "body": body}
                outer.requests.append(request)
                answer = outer.handler(request)
                if answer is None:  # never answer (timeout test)
                    threading.Event().wait(2)
                    return
                status, payload, headers = answer
                data = payload if isinstance(payload, str) else json.dumps(payload)
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                for name, value in headers.items():
                    self.send_header(name, value)
                self.send_header("Content-Length", str(len(data.encode())))
                self.end_headers()
                self.wfile.write(data.encode())

            do_GET = do_POST = do_DELETE = _serve

            def log_message(self, *args):
                pass

        self.httpd = HTTPServer(("127.0.0.1", 0), Handler)
        self.url = f"http://127.0.0.1:{self.httpd.server_port}"
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def close(self):
        self.httpd.shutdown()
        self.httpd.server_close()


@pytest.fixture()
def server():
    instance = Server()
    yield instance
    instance.close()


def scan_body(state, complete, error=None):
    return {
        "id": "11111111-1111-1111-1111-111111111111",
        "state": state,
        "token": {"kind": "asa", "tokenId": 1},
        "round": 5,
        "includeZero": False,
        "holdersFound": 2,
        "complete": complete,
        "error": error,
        "createdAt": "2026-10-14T10:00:00Z",
        "updatedAt": "2026-10-14T10:00:00Z",
        "expiresAt": "2026-10-15T10:00:00Z",
    }


def holder(account):
    return {"account": account, "amount": "5", "optedIn": True, "lastChangedRound": 7}


def test_defaults_are_the_public_deployments():
    assert AvmBalanceIndexer(chain=ALGORAND_TESTNET).base_url == "https://testnet.balance-indexer.biatec.io"
    assert AvmBalanceIndexer.algorand_mainnet().base_url == "https://mainnet.balance-indexer.biatec.io"
    assert AvmBalanceIndexer.voi().base_url == "https://voi.balance-indexer.biatec.io"
    assert AvmBalanceIndexer().chain == ALGORAND_MAINNET
    assert sorted(DEFAULT_BASE_URLS) == sorted(CHAINS)


def test_custom_base_url_is_honoured_and_trimmed():
    assert AvmBalanceIndexer(chain=VOI, base_url="https://my.example.com/indexer//").base_url == "https://my.example.com/indexer"


def test_unknown_chain_is_rejected():
    with pytest.raises(ValueError):
        AvmBalanceIndexer(chain="solana")


def test_chain_is_bound_into_the_path_and_headers_are_sent(server):
    server.handler = lambda r: (
        200,
        {"chain": "voi", "provider": "Cassandra", "state": "Following", "indexedRound": 10, "targetRound": 11, "lag": 1, "updatedAt": "2026-10-14T10:00:00Z"},
        {},
    )
    client = AvmBalanceIndexer.voi(base_url=server.url, headers={"X-Api-Key": "k"})

    status = client.status()

    assert status.indexed_round == 10
    request = server.requests[0]
    assert request["path"] == "/v1/voi/status"
    assert request["headers"]["X-Api-Key"] == "k"
    assert "avm-balance-indexer" in request["headers"]["User-Agent"]


def test_query_parameters(server):
    server.handler = lambda r: (200, {"token": {"kind": "asa", "tokenId": 31566704}, "round": 9, "indexedRound": 9, "items": [], "nextCursor": None}, {})
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    client.holders("asa", 31566704, round=9, include_zero=True, limit=50, cursor="abc")

    url = urlparse(server.requests[0]["path"])
    assert url.path == "/v1/algorand-mainnet/tokens/asa/31566704/holders"
    assert {k: v[0] for k, v in parse_qs(url.query).items()} == {"round": "9", "includeZero": "true", "limit": "50", "cursor": "abc"}


def test_invalid_arguments_never_reach_the_server(server):
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)
    with pytest.raises(ValueError):
        client.token("nft", 1)
    with pytest.raises(ValueError):
        client.token("asa", -1)
    with pytest.raises(ValueError):
        client.token("asa", "abc")
    assert server.requests == []


@pytest.mark.parametrize("limit", [0, 1001, 1.5, True, "10"])
def test_a_page_size_outside_1_to_1000_never_reaches_the_server(server, limit):
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)
    with pytest.raises(ValueError):
        client.holders("asa", 1, limit=limit)
    with pytest.raises(ValueError):
        client.account_tokens("ADDRESS", limit=limit)
    assert server.requests == []


def test_scan_request_is_posted(server):
    server.handler = lambda r: (202, scan_body("Pending", False), {})
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    scan = client.start_holder_scan("asa", 1, round=5)

    assert scan.state == "Pending"
    assert server.requests[0]["method"] == "POST"
    assert json.loads(server.requests[0]["body"]) == {"round": 5, "includeZero": None}


def test_error_status_becomes_api_error_with_retry_after(server):
    server.handler = lambda r: (429, {"error": "slow down"}, {"Retry-After": "7"})
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    with pytest.raises(ApiError) as caught:
        client.status()

    assert caught.value.status == 429
    assert caught.value.message == "slow down"
    assert caught.value.retry_after_seconds == 7
    assert caught.value.is_retryable


def test_retry_after_may_be_an_http_date_and_nonsense_is_ignored(server):
    from email.utils import formatdate
    import time as clock

    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)
    answers = {}
    for label, value in (("date", formatdate(clock.time() + 30, usegmt=True)), ("inf", "inf"), ("soon", "soon")):
        server.handler = lambda r, v=value: (429, {"error": "slow down"}, {"Retry-After": v})
        with pytest.raises(ApiError) as caught:
            client.status()
        answers[label] = caught.value.retry_after_seconds

    assert answers["date"] is not None and 0 < answers["date"] <= 30
    assert answers["inf"] is None and answers["soon"] is None


def test_argument_validation_errors_are_not_reported_as_decode_errors(server):
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)
    with pytest.raises(ValueError) as caught:
        client.account_tokens("ADDRESS", cursor=123)
    assert not isinstance(caught.value, ResponseDecodeError)
    assert server.requests == []


def test_a_redirect_is_followed(server):
    server.handler = lambda r: (302, "", {"Location": "/moved"}) if r["path"] == "/v1/algorand-mainnet/status" else (
        200, {"chain": "algorand-mainnet", "provider": "x", "state": "Following", "indexedRound": 1, "targetRound": 1, "lag": 0, "updatedAt": None}, {})
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    assert client.status().state == "Following"


def test_not_found_and_non_json_body(server):
    server.handler = lambda r: (404, "nope", {})
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    with pytest.raises(ApiError) as caught:
        client.token("asa", 1)

    assert caught.value.is_not_found
    assert caught.value.body == "nope"


def test_a_success_answer_that_is_not_json_is_a_decode_error(server):
    server.handler = lambda r: (200, "<html>captive portal</html>", {})
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    with pytest.raises(ResponseDecodeError):
        client.status()


def test_refused_connection_becomes_connection_error():
    client = AvmBalanceIndexer.algorand_mainnet(base_url="http://127.0.0.1:1", timeout=2)
    with pytest.raises(IndexerConnectionError):
        client.status()


def test_timeout_becomes_connection_error(server):
    server.handler = lambda r: None
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url, timeout=0.2)
    with pytest.raises(IndexerConnectionError):
        client.status()


def test_iter_holders_walks_pages_and_pins_the_round(server):
    def handler(request):
        query = parse_qs(urlparse(request["path"]).query)
        cursor = query.get("cursor", [None])[0]

        def page(account, nxt):
            return {"token": {"kind": "asa", "tokenId": 1}, "round": 42, "indexedRound": 50, "items": [holder(account)], "nextCursor": nxt}

        if cursor is None:
            return 200, page("A", "c1"), {}
        if cursor == "c1":
            return 200, page("B", "c2"), {}
        return 200, page("C", None), {}

    server.handler = handler
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    accounts = [h.account for h in client.iter_holders("asa", 1)]

    assert accounts == ["A", "B", "C"]
    rounds = [parse_qs(urlparse(r["path"]).query).get("round") for r in server.requests]
    assert rounds == [None, ["42"], ["42"]]


def test_export_holders_waits_for_the_scan_and_keeps_its_cursor(server):
    state = {"waited": False}
    scan_id = "11111111-1111-1111-1111-111111111111"

    def handler(request):
        parsed = urlparse(request["path"])
        if request["method"] == "POST":
            return 202, scan_body("Pending", False), {}
        if parsed.path.endswith("/holders"):
            cursor = parse_qs(parsed.query).get("cursor", [None])[0]
            if cursor is None:
                return 200, {"id": scan_id, "state": "Running", "complete": False, "round": 5, "items": [holder("A")], "nextCursor": "c1"}, {}
            if not state["waited"]:
                # Caught up with a running scan: no new rows, and the server answers with the position to continue from.
                state["waited"] = True
                return 200, {"id": scan_id, "state": "Running", "complete": False, "round": 5, "items": [], "nextCursor": "c1"}, {}
            return 200, {"id": scan_id, "state": "Completed", "complete": True, "round": 5, "items": [holder("B")], "nextCursor": None}, {}
        return 200, scan_body("Running", False), {}

    server.handler = handler
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    assert [h.account for h in client.export_holders("asa", 1, poll_interval=0.01)] == ["A", "B"]
    cursors = [parse_qs(urlparse(r["path"]).query).get("cursor", [None])[0] for r in server.requests if urlparse(r["path"]).path.endswith("/holders")]
    assert cursors == [None, "c1", "c1"]
    # Caught up with a running scan: it looks at the scan and waits instead of asking for the next page at once.
    assert any(r["method"] == "GET" and urlparse(r["path"]).path.endswith(f"/scans/{scan_id}") for r in server.requests)


def test_the_last_page_of_a_scan_that_finished_between_two_server_reads_is_not_read_twice(server):
    def handler(request):
        parsed = urlparse(request["path"])
        if request["method"] == "POST":
            return 202, scan_body("Pending", False), {}
        if parsed.path.endswith("/holders"):
            # Rows, no cursor and not complete: the job looked running when the endpoint read it and was finished when the store did.
            return 200, {"id": "s", "state": "Running", "complete": False, "round": 5, "items": [holder("A"), holder("B")], "nextCursor": None}, {}
        return 200, scan_body("Completed", True), {}

    server.handler = handler
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    assert [h.account for h in client.export_holders("asa", 1, poll_interval=0.01)] == ["A", "B"]


def test_stopping_an_export_early_cancels_the_scan(server):
    def handler(request):
        if request["method"] == "POST":
            return 202, scan_body("Pending", False), {}
        if request["method"] == "DELETE":
            return 200, scan_body("Cancelled", False), {}
        return 200, {"id": "s", "state": "Running", "complete": False, "round": 5, "items": [holder("A"), holder("B")], "nextCursor": "c1"}, {}

    server.handler = handler
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    export = client.export_holders("asa", 1, poll_interval=0.01)
    assert next(export).account == "A"
    export.close()

    assert any(r["method"] == "DELETE" for r in server.requests)


def test_export_validates_the_page_size_before_it_starts_a_scan(server):
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)
    with pytest.raises(ValueError):
        list(client.export_holders("asa", 1, limit=0))
    assert server.requests == []


def test_stopping_inside_the_last_page_does_not_cancel_a_finished_scan(server):
    def handler(request):
        if request["method"] == "POST":
            return 202, scan_body("Pending", False), {}
        return 200, {"id": "s", "state": "Completed", "complete": True, "round": 5, "items": [holder("A"), holder("B")], "nextCursor": None}, {}

    server.handler = handler
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    export = client.export_holders("asa", 1)
    assert next(export).account == "A"
    export.close()

    assert not any(r["method"] == "DELETE" for r in server.requests)


@pytest.mark.parametrize("bad", ["", " ", "1.5", 1.9, True, None, -1])
def test_blank_or_non_integer_ids_are_rejected(server, bad):
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)
    with pytest.raises(ValueError):
        client.token("asa", bad)
    assert server.requests == []


def test_export_holders_raises_when_the_scan_failed(server):
    def handler(request):
        path = urlparse(request["path"]).path
        if request["method"] == "POST":
            return 202, scan_body("Failed", False, "boom"), {}
        if path.endswith("/holders"):
            return 200, {"id": "s", "state": "Failed", "complete": False, "round": 5, "items": [], "nextCursor": None}, {}
        return 200, scan_body("Failed", False, "boom"), {}

    server.handler = handler
    client = AvmBalanceIndexer.algorand_mainnet(base_url=server.url)

    with pytest.raises(ScanError, match="Failed: boom"):
        list(client.export_holders("asa", 1, poll_interval=0.01))

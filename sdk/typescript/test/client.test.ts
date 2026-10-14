import assert from "node:assert/strict";
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import type { AddressInfo } from "node:net";
import { after, before, beforeEach, describe, it } from "node:test";
import { ApiError, AvmBalanceIndexer, Chains, ConnectionError, DEFAULT_BASE_URLS, DecodeError, ScanError } from "../src/index.js";

type Handler = (request: IncomingMessage, response: ServerResponse) => void;

let server: Server;
let baseUrl: string;
let handler: Handler;
let requests: { method: string; url: string; headers: IncomingMessage["headers"]; body: string }[];

function json(response: ServerResponse, status: number, body: unknown, headers: Record<string, string> = {}): void {
  response.writeHead(status, { "content-type": "application/json", ...headers });
  response.end(JSON.stringify(body));
}

function scanBody(state: string, complete: boolean, error: string | null = null) {
  return {
    id: "s1",
    state,
    token: { kind: "asa", tokenId: 1 },
    round: 5,
    includeZero: false,
    holdersFound: 2,
    complete,
    error,
    createdAt: "2026-10-14T10:00:00Z",
    updatedAt: "2026-10-14T10:00:00Z",
    expiresAt: "2026-10-15T10:00:00Z",
  };
}

before(async () => {
  server = createServer((request, response) => {
    let body = "";
    request.on("data", (chunk) => (body += chunk));
    request.on("end", () => {
      requests.push({ method: request.method ?? "", url: request.url ?? "", headers: request.headers, body });
      handler(request, response);
    });
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  baseUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

after(() => {
  server.closeAllConnections();
  server.close();
});

beforeEach(() => {
  requests = [];
  handler = (_request, response) => json(response, 500, { error: "unexpected" });
});

const holder = (account: string) => ({ account, amount: "5", optedIn: true, lastChangedRound: 7 });

describe("defaults", () => {
  it("uses the public deployment of each chain", () => {
    assert.equal(new AvmBalanceIndexer({ chain: Chains.AlgorandTestnet }).baseUrl, "https://testnet.balance-indexer.biatec.io");
    assert.equal(AvmBalanceIndexer.algorandMainnet().baseUrl, "https://mainnet.balance-indexer.biatec.io");
    assert.equal(AvmBalanceIndexer.voi().baseUrl, "https://voi.balance-indexer.biatec.io");
    assert.equal(new AvmBalanceIndexer().chain, "algorand-mainnet");
    assert.deepEqual(Object.keys(DEFAULT_BASE_URLS).sort(), ["algorand-mainnet", "algorand-testnet", "voi"]);
  });

  it("honours a custom base URL and trims trailing slashes", () => {
    assert.equal(new AvmBalanceIndexer({ chain: Chains.Voi, baseUrl: "https://my.example.com/indexer//" }).baseUrl, "https://my.example.com/indexer");
  });

  it("rejects an unknown chain", () => {
    assert.throws(() => new AvmBalanceIndexer({ chain: "solana" as never }), RangeError);
  });
});

describe("requests", () => {
  it("binds the chain into the path and sends custom headers", async () => {
    handler = (_request, response) =>
      json(response, 200, { chain: "voi", provider: "Cassandra", state: "Following", indexedRound: 10, targetRound: 11, lag: 1, updatedAt: "2026-10-14T10:00:00Z" });
    const client = AvmBalanceIndexer.voi({ baseUrl, headers: { "X-Api-Key": "k" } });

    const status = await client.status();

    assert.equal(status.indexedRound, 10);
    assert.equal(requests[0]?.url, "/v1/voi/status");
    assert.equal(requests[0]?.headers["x-api-key"], "k");
  });

  it("serialises query parameters", async () => {
    handler = (_request, response) => json(response, 200, { token: { kind: "asa", tokenId: 31566704 }, round: 9, indexedRound: 9, items: [], nextCursor: null });
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    await client.holders("asa", 31566704, { round: 9, includeZero: true, limit: 50, cursor: "abc" });

    const url = new URL(requests[0]!.url, "http://x");
    assert.equal(url.pathname, "/v1/algorand-mainnet/tokens/asa/31566704/holders");
    assert.deepEqual(Object.fromEntries(url.searchParams), { round: "9", includeZero: "true", limit: "50", cursor: "abc" });
  });

  it("rejects ids that do not fit a JavaScript number", async () => {
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });
    await assert.rejects(() => client.token("asa", 2n ** 60n), RangeError);
    await assert.rejects(() => client.token("asa", -1), RangeError);
    assert.equal(requests.length, 0);
  });

  it("posts a scan request", async () => {
    handler = (_request, response) => json(response, 202, scanBody("Pending", false));
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    const scan = await client.startHolderScan("asa", 1, { round: 5 });

    assert.equal(scan.state, "Pending");
    assert.equal(requests[0]?.method, "POST");
    assert.deepEqual(JSON.parse(requests[0]!.body), { round: 5, includeZero: null });
  });
});

describe("errors", () => {
  it("maps an error status to ApiError with the server message and Retry-After", async () => {
    handler = (_request, response) => json(response, 429, { error: "slow down" }, { "Retry-After": "7" });
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    await assert.rejects(
      () => client.status(),
      (error: unknown) => {
        assert.ok(error instanceof ApiError);
        assert.equal(error.status, 429);
        assert.equal(error.message, "slow down");
        assert.equal(error.retryAfterSeconds, 7);
        assert.equal(error.isRetryable, true);
        return true;
      },
    );
  });

  it("understands Retry-After as an HTTP date and ignores nonsense", async () => {
    const answer = (retryAfter: string) => {
      handler = (_request, response) => json(response, 429, { error: "slow down" }, { "Retry-After": retryAfter });
      return AvmBalanceIndexer.algorandMainnet({ baseUrl }).status().then(
        () => undefined,
        (error: unknown) => (error as ApiError).retryAfterSeconds,
      );
    };

    const inThirtySeconds = await answer(new Date(Date.now() + 30_000).toUTCString());
    assert.ok(inThirtySeconds !== undefined && inThirtySeconds > 0 && inThirtySeconds <= 30);
    assert.equal(await answer("Infinity"), undefined);
    assert.equal(await answer("soon"), undefined);
  });

  it("reports 404 as not found and keeps non-JSON bodies", async () => {
    handler = (_request, response) => {
      response.writeHead(404, { "content-type": "text/plain" });
      response.end("nope");
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    await assert.rejects(
      () => client.token("asa", 1),
      (error: unknown) => error instanceof ApiError && error.isNotFound && error.body === "nope",
    );
  });

  it("maps a success answer that is not JSON to DecodeError", async () => {
    handler = (_request, response) => {
      response.writeHead(200, { "content-type": "text/html" });
      response.end("<html>captive portal</html>");
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    await assert.rejects(() => client.status(), DecodeError);
  });

  it("maps a refused connection to ConnectionError", async () => {
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl: "http://127.0.0.1:1" });
    await assert.rejects(() => client.status(), ConnectionError);
  });

  it("passes a caller's abort through unchanged", async () => {
    handler = () => {
      // never answers
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });
    const controller = new AbortController();
    setTimeout(() => controller.abort(), 50);

    await assert.rejects(
      () => client.status(controller.signal),
      (error: unknown) => !(error instanceof ConnectionError) && error instanceof Error && error.name === "AbortError",
    );
  });

  it("passes a caller's custom abort reason through unchanged", async () => {
    handler = () => {
      // never answers
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });
    const controller = new AbortController();
    const reason = new Error("user left");
    setTimeout(() => controller.abort(reason), 50);

    await assert.rejects(
      () => client.status(controller.signal),
      (error: unknown) => error === reason,
    );
  });

  it("times out while the body is being read", async () => {
    handler = (_request, response) => {
      response.writeHead(200, { "content-type": "application/json", "content-length": "1000" });
      response.write("{");
      // the rest never comes
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl, timeoutMs: 200 });
    await assert.rejects(() => client.status(), ConnectionError);
  });

  it("times out", async () => {
    handler = () => {
      // never answers
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl, timeoutMs: 100 });
    await assert.rejects(() => client.status(), ConnectionError);
  });
});

describe("pagination", () => {
  it("walks all holder pages and pins the round of the first page", async () => {
    handler = (request, response) => {
      const url = new URL(request.url ?? "", "http://x");
      const cursor = url.searchParams.get("cursor");
      const page = (account: string, next: string | null) => ({ token: { kind: "asa", tokenId: 1 }, round: 42, indexedRound: 50, items: [holder(account)], nextCursor: next });
      if (cursor === null) json(response, 200, page("A", "c1"));
      else if (cursor === "c1") json(response, 200, page("B", "c2"));
      else json(response, 200, page("C", null));
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    const accounts: string[] = [];
    for await (const item of client.iterateHolders("asa", 1)) {
      accounts.push(item.account);
    }

    assert.deepEqual(accounts, ["A", "B", "C"]);
    assert.equal(new URL(requests[0]!.url, "http://x").searchParams.get("round"), null);
    assert.equal(new URL(requests[1]!.url, "http://x").searchParams.get("round"), "42");
    assert.equal(new URL(requests[2]!.url, "http://x").searchParams.get("round"), "42");
  });

  it("exports holders through a scan, waiting while it runs and keeping the cursor", async () => {
    let waited = false;
    const id = "11111111-1111-1111-1111-111111111111";
    handler = (request, response) => {
      const url = new URL(request.url ?? "", "http://x");
      if (request.method === "POST") {
        json(response, 202, scanBody("Pending", false));
      } else if (url.pathname.endsWith("/holders")) {
        const cursor = url.searchParams.get("cursor");
        if (cursor === null) json(response, 200, { id, state: "Running", complete: false, round: 5, items: [holder("A")], nextCursor: "c1" });
        else if (!waited) {
          // Caught up with a running scan: no new rows, and the server answers with the position to continue from.
          waited = true;
          json(response, 200, { id, state: "Running", complete: false, round: 5, items: [], nextCursor: "c1" });
        } else json(response, 200, { id, state: "Completed", complete: true, round: 5, items: [holder("B")], nextCursor: null });
      } else {
        json(response, 200, scanBody("Running", false));
      }
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    const accounts: string[] = [];
    for await (const item of client.exportHolders("asa", 1, { pollIntervalMs: 10 })) {
      accounts.push(item.account);
    }

    assert.deepEqual(accounts, ["A", "B"]);
    const pageCursors = requests.filter((r) => r.method === "GET" && new URL(r.url, "http://x").pathname.endsWith("/holders")).map((r) => new URL(r.url, "http://x").searchParams.get("cursor"));
    assert.deepEqual(pageCursors, [null, "c1", "c1"]);
    // Caught up with a running scan: it looks at the scan and waits instead of asking for the next page at once.
    assert.ok(requests.some((r) => r.method === "GET" && new URL(r.url, "http://x").pathname.endsWith("/scans/s1")));
  });

  it("cancels the scan when the consumer stops early", async () => {
    const id = "11111111-1111-1111-1111-111111111111";
    handler = (request, response) => {
      const url = new URL(request.url ?? "", "http://x");
      if (request.method === "POST") json(response, 202, scanBody("Pending", false));
      else if (request.method === "DELETE") json(response, 200, scanBody("Cancelled", false));
      else json(response, 200, { id, state: "Running", complete: false, round: 5, items: [holder("A"), holder("B")], nextCursor: "c1" });
      void url;
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    for await (const _ of client.exportHolders("asa", 1, { pollIntervalMs: 10 })) {
      break;
    }

    assert.ok(requests.some((r) => r.method === "DELETE"));
  });

  it("rejects a page size outside 1 to 1000 before sending anything", async () => {
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });
    await assert.rejects(() => client.holders("asa", 1, { limit: 0 }), RangeError);
    await assert.rejects(() => client.accountTokens("A", { limit: 5000 }), RangeError);
    await assert.rejects(() => client.holders("asa", 1, { limit: 1.5 }), RangeError);
    assert.equal(requests.length, 0);
  });

  it("validates the page size of an export before it starts a scan", async () => {
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });
    await assert.rejects(async () => {
      for await (const _ of client.exportHolders("asa", 1, { limit: 0 })) {
        // never reached
      }
    }, RangeError);
    assert.equal(requests.length, 0);
  });

  it("does not cancel a scan that has finished when the consumer stops inside the last page", async () => {
    handler = (request, response) => {
      if (request.method === "POST") json(response, 202, scanBody("Pending", false));
      else json(response, 200, { id: "s1", state: "Completed", complete: true, round: 5, items: [holder("A"), holder("B")], nextCursor: null });
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    for await (const _ of client.exportHolders("asa", 1)) {
      break;
    }

    assert.ok(!requests.some((r) => r.method === "DELETE"));
  });

  it("rejects inherited property names as chains", () => {
    assert.throws(() => new AvmBalanceIndexer({ chain: "constructor" as never }), RangeError);
  });

  it("rejects blank and non-numeric ids", async () => {
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });
    await assert.rejects(() => client.token("asa", ""), RangeError);
    await assert.rejects(() => client.token("asa", " "), RangeError);
    await assert.rejects(() => client.token("asa", "1.5"), RangeError);
    await assert.rejects(() => client.token("asa", true as never), RangeError);
    assert.equal(requests.length, 0);
  });

  it("fails an export whose scan failed", async () => {
    handler = (request, response) => {
      const url = new URL(request.url ?? "", "http://x");
      if (request.method === "POST") json(response, 202, scanBody("Failed", false, "boom"));
      else if (url.pathname.endsWith("/holders")) json(response, 200, { id: "s1", state: "Failed", complete: false, round: 5, items: [], nextCursor: null });
      else json(response, 200, scanBody("Failed", false, "boom"));
    };
    const client = AvmBalanceIndexer.algorandMainnet({ baseUrl });

    await assert.rejects(async () => {
      for await (const _ of client.exportHolders("asa", 1, { pollIntervalMs: 10 })) {
        // drain
      }
    }, (error: unknown) => error instanceof ScanError && error.scan.state === "Failed" && /Failed: boom/.test(error.message));
  });
});

import {
  BalancesApi,
  Configuration,
  FetchError,
  HistoryApi,
  HoldersApi,
  ResponseError,
  ScansApi,
  StatusApi,
  TokensApi,
  ChainId,
  TokenKind,
  type BalanceResponse,
  type EffectDto,
  type HistoryResponse,
  type HolderDto,
  type HoldersResponse,
  type HoldingDto,
  type HoldingsResponse,
  type ScanHoldersResponse,
  type ScanResponse,
  type StatusResponse,
  type TokenResponse,
} from "./generated/index.js";

/** The chains an AVM Balance Indexer deployment can serve: `Chains.Voi` is `"voi"` (generated from the API description). */
export const Chains = ChainId;
export { ChainId, TokenKind };

/** Public deployments, used when no `baseUrl` is given. A new chain in the API description fails to compile here. */
export const DEFAULT_BASE_URLS: Readonly<Record<ChainId, string>> = {
  "algorand-mainnet": "https://mainnet.balance-indexer.biatec.io",
  "algorand-testnet": "https://testnet.balance-indexer.biatec.io",
  voi: "https://voi.balance-indexer.biatec.io",
};

/** Token ids and rounds: numbers, or bigint / numeric strings, up to 2^53 - 1 (the generated API types them as JavaScript numbers); larger values are rejected with a RangeError. */
export type Id = number | bigint | string;

export interface ClientOptions {
  /** Which chain the indexer at `baseUrl` serves. Default: `algorand-mainnet`. */
  chain?: ChainId;
  /** Root URL of an indexer deployment (no `/v1/...`). Default: the public deployment of `chain`. */
  baseUrl?: string;
  /** Extra headers on every request (for example an API key of your own gateway). */
  headers?: Record<string, string>;
  /** Request timeout in milliseconds. Default 30000; 0 disables it. */
  timeoutMs?: number;
  /** Your own `fetch` (proxies, tests, polyfills). Default: the global one. */
  fetch?: typeof fetch;
}

export interface PageOptions {
  /** Page size, 1 to 1000 (server default 100). */
  limit?: number;
  /** Cursor of the previous page (`nextCursor`). */
  cursor?: string;
  signal?: AbortSignal;
}

export interface HoldersOptions extends PageOptions {
  /** End-of-round state to read. Default: the latest published round. */
  round?: Id;
  /** Also list opted-in accounts that hold zero. */
  includeZero?: boolean;
}

export interface HistoryOptions extends PageOptions {
  fromRound?: Id;
  toRound?: Id;
}

export interface AccountTokensOptions extends PageOptions {
  includeZero?: boolean;
}

export interface ExportHoldersOptions {
  round?: Id;
  includeZero?: boolean;
  /** Milliseconds between polls of the scan. Default 1000. */
  pollIntervalMs?: number;
  /** Page size for reading the result. */
  limit?: number;
  signal?: AbortSignal;
}

/** The server answered with an error status. */
export class ApiError extends Error {
  override readonly name = "ApiError";
  constructor(
    /** HTTP status code. */
    readonly status: number,
    /** Message from the API (`error` field) or the status text. */
    message: string,
    /** Seconds to wait before retrying, from `Retry-After` (429 and 503). */
    readonly retryAfterSeconds?: number,
    /** Raw response body. */
    readonly body?: string,
  ) {
    super(message);
  }

  /** The requested thing does not exist (unknown token, round beyond the indexed one, wrong chain). */
  get isNotFound(): boolean {
    return this.status === 404;
  }

  /** Rate limited, or nothing indexed yet: retry after `retryAfterSeconds`. */
  get isRetryable(): boolean {
    return this.status === 429 || this.status === 503 || this.status === 504;
  }
}

/** The server answered with a success status but the body is not what the API documents (for example an HTML page of a proxy). */
export class DecodeError extends Error {
  override readonly name = "DecodeError";
  constructor(message: string, override readonly cause?: unknown) {
    super(message);
  }
}

/** The request never produced a response (network failure or timeout). */
export class ConnectionError extends Error {
  override readonly name = "ConnectionError";
  constructor(message: string, override readonly cause?: unknown) {
    super(message);
  }
}

/** A holder scan ended as `Failed` or `Cancelled` before it finished. */
export class ScanError extends Error {
  override readonly name = "ScanError";
  constructor(
    message: string,
    /** The scan as last reported by the server. */
    readonly scan: ScanResponse,
  ) {
    super(message);
  }
}

/** Carries the reason of a caller's abort through the generated runtime, which wraps every fetch failure. */
class CallerAborted extends Error {
  constructor(readonly reason: unknown) {
    super("aborted by the caller");
  }
}

const TERMINAL_SCAN_STATES = new Set(["Completed", "Failed", "Cancelled"]);

function toNumber(value: Id, what: string): number {
  // Only numbers, bigints and strings of digits: Number("") is 0 and would silently turn a blank id into token 0.
  const valid = typeof value === "number" || typeof value === "bigint" || (typeof value === "string" && /^\d+$/.test(value));
  const n = valid ? Number(value) : Number.NaN;
  if (!Number.isSafeInteger(n) || n < 0) {
    throw new RangeError(`${what} must be a non-negative integer up to 2^53 - 1 (got ${String(value)})`);
  }
  return n;
}

function checkLimit(limit: number | undefined): number | undefined {
  if (limit !== undefined && (!Number.isInteger(limit) || limit < 1 || limit > 1000)) {
    throw new RangeError(`limit must be an integer from 1 to 1000 (got ${String(limit)})`);
  }
  return limit;
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(signal.reason);
      return;
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener("abort", onAbort);
      resolve();
    }, ms);
    const onAbort = () => {
      clearTimeout(timer);
      reject(signal?.reason);
    };
    signal?.addEventListener("abort", onAbort, { once: true });
  });
}

/**
 * Client for one chain of an AVM Balance Indexer deployment.
 *
 * ```ts
 * const indexer = new AvmBalanceIndexer({ chain: Chains.Voi });          // public Voi deployment
 * const custom = new AvmBalanceIndexer({ chain: Chains.AlgorandMainnet, baseUrl: "https://indexer.example.com" });
 * ```
 */
export class AvmBalanceIndexer {
  readonly chain: ChainId;
  readonly baseUrl: string;

  private readonly status_: StatusApi;
  private readonly balances: BalancesApi;
  private readonly historyApi: HistoryApi;
  private readonly holdersApi: HoldersApi;
  private readonly tokens: TokensApi;
  private readonly scans: ScansApi;

  constructor(options: ClientOptions = {}) {
    this.chain = options.chain ?? Chains.AlgorandMainnet;
    if (!Object.hasOwn(DEFAULT_BASE_URLS, this.chain)) {
      throw new RangeError(`Unknown chain "${String(this.chain)}"; use one of ${Object.values(Chains).join(", ")}`);
    }
    this.baseUrl = (options.baseUrl ?? DEFAULT_BASE_URLS[this.chain]).replace(/\/+$/, "");
    if (!URL.canParse(this.baseUrl) || !/^https?:$/.test(new URL(this.baseUrl).protocol)) {
      throw new RangeError(`baseUrl must be an absolute http or https URL (got "${this.baseUrl}")`);
    }
    const timeoutMs = options.timeoutMs ?? 30_000;
    const inner = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => fetch(input, init));
    const configuration = new Configuration({
      basePath: this.baseUrl,
      headers: { "User-Agent": "avm-balance-indexer-typescript-sdk", ...options.headers },
      fetchApi: async (input, init) => {
        const caller = init?.signal ?? undefined;
        try {
          if (timeoutMs > 0) {
            const timeout = AbortSignal.timeout(timeoutMs);
            return await inner(input, { ...init, signal: caller ? AbortSignal.any([caller, timeout]) : timeout });
          }
          return await inner(input, init);
        } catch (error) {
          // The caller's own abort (with whatever reason) is a cancellation, not a connection problem.
          if (caller?.aborted) {
            throw new CallerAborted(caller.reason);
          }
          throw error;
        }
      },
    });
    this.status_ = new StatusApi(configuration);
    this.balances = new BalancesApi(configuration);
    this.historyApi = new HistoryApi(configuration);
    this.holdersApi = new HoldersApi(configuration);
    this.tokens = new TokensApi(configuration);
    this.scans = new ScansApi(configuration);
  }

  /** Public MainNet deployment (or `baseUrl`). */
  static algorandMainnet(options: Omit<ClientOptions, "chain"> = {}): AvmBalanceIndexer {
    return new AvmBalanceIndexer({ ...options, chain: Chains.AlgorandMainnet });
  }

  /** Public TestNet deployment (or `baseUrl`). */
  static algorandTestnet(options: Omit<ClientOptions, "chain"> = {}): AvmBalanceIndexer {
    return new AvmBalanceIndexer({ ...options, chain: Chains.AlgorandTestnet });
  }

  /** Public Voi deployment (or `baseUrl`). */
  static voi(options: Omit<ClientOptions, "chain"> = {}): AvmBalanceIndexer {
    return new AvmBalanceIndexer({ ...options, chain: Chains.Voi });
  }

  private async call<T>(run: () => Promise<T>): Promise<T> {
    try {
      const value = await run();
      if (value === null || value === undefined) {
        throw new DecodeError(`Unexpected response from ${this.baseUrl}: the body is empty`);
      }
      return value;
    } catch (error) {
      if (error instanceof DecodeError) {
        throw error;
      }
      if (error instanceof TypeError) {
        // The generated mapping met JSON of another shape (for example {"error": "x"} where a list is documented).
        throw new DecodeError(`Unexpected response from ${this.baseUrl}: ${error.message}`, error);
      }
      if (error instanceof ResponseError) {
        throw await toApiError(error.response);
      }
      if (error instanceof DOMException && error.name === "TimeoutError") {
        // The timeout stays armed until the body is read: a slow body is a timeout like a slow connection.
        throw new ConnectionError(`Request to ${this.baseUrl} timed out`, error);
      }
      if (error instanceof SyntaxError) {
        throw new DecodeError(`Unexpected response from ${this.baseUrl}: ${error.message}`, error);
      }
      if (error instanceof FetchError) {
        // A caller's own abort is a cancellation, not a connection problem: hand its reason back unchanged.
        if (error.cause instanceof CallerAborted) {
          throw error.cause.reason;
        }
        if (error.cause instanceof DOMException && error.cause.name === "TimeoutError") {
          throw new ConnectionError(`Request to ${this.baseUrl} timed out`, error.cause);
        }
        throw new ConnectionError(`Request to ${this.baseUrl} failed: ${error.cause instanceof Error ? error.cause.message : error.message}`, error.cause);
      }
      throw error;
    }
  }

  /** Indexed round, target round, lag and ingestion state. */
  status(signal?: AbortSignal): Promise<StatusResponse> {
    return this.call(() => this.status_.getChainStatus({ chain: this.chain }, { signal }));
  }

  /** Current holdings of an account (one page). */
  accountTokens(address: string, options: AccountTokensOptions = {}): Promise<HoldingsResponse> {
    return this.call(() =>
      this.balances.getAccountTokens(
        { chain: this.chain, address, includeZero: options.includeZero, cursor: options.cursor, limit: checkLimit(options.limit) },
        { signal: options.signal },
      ),
    );
  }

  /** End-of-round balance of an account and token. Amounts are decimal strings. */
  balance(address: string, kind: TokenKind, tokenId: Id, options: { round?: Id; signal?: AbortSignal } = {}): Promise<BalanceResponse> {
    return this.call(() =>
      this.balances.getTokenBalance(
        {
          chain: this.chain,
          address,
          kind,
          tokenId: toNumber(tokenId, "tokenId"),
          round: options.round === undefined ? undefined : toNumber(options.round, "round"),
        },
        { signal: options.signal },
      ),
    );
  }

  /** Balance effects of an account and token, newest first (one page). */
  history(address: string, kind: TokenKind, tokenId: Id, options: HistoryOptions = {}): Promise<HistoryResponse> {
    return this.call(() =>
      this.historyApi.getTokenHistory(
        {
          chain: this.chain,
          address,
          kind,
          tokenId: toNumber(tokenId, "tokenId"),
          fromRound: options.fromRound === undefined ? undefined : toNumber(options.fromRound, "fromRound"),
          toRound: options.toRound === undefined ? undefined : toNumber(options.toRound, "toRound"),
          cursor: options.cursor,
          limit: checkLimit(options.limit),
        },
        { signal: options.signal },
      ),
    );
  }

  /** Holders of a token at the end of a round (one page, in account order). */
  holders(kind: TokenKind, tokenId: Id, options: HoldersOptions = {}): Promise<HoldersResponse> {
    return this.call(() =>
      this.holdersApi.getTokenHolders(
        {
          chain: this.chain,
          kind,
          tokenId: toNumber(tokenId, "tokenId"),
          round: options.round === undefined ? undefined : toNumber(options.round, "round"),
          includeZero: options.includeZero,
          cursor: options.cursor,
          limit: checkLimit(options.limit),
        },
        { signal: options.signal },
      ),
    );
  }

  /** Token metadata (symbol, name, decimals, standard). */
  token(kind: TokenKind, tokenId: Id, signal?: AbortSignal): Promise<TokenResponse> {
    return this.call(() => this.tokens.getToken({ chain: this.chain, kind, tokenId: toNumber(tokenId, "tokenId") }, { signal }));
  }

  /** Starts an asynchronous export of every holder of a token. Prefer {@link exportHolders}. */
  startHolderScan(kind: TokenKind, tokenId: Id, options: { round?: Id; includeZero?: boolean; signal?: AbortSignal } = {}): Promise<ScanResponse> {
    return this.call(() =>
      this.scans.createHolderScan(
        {
          chain: this.chain,
          kind,
          tokenId: toNumber(tokenId, "tokenId"),
          scanRequest: {
            round: options.round === undefined ? null : toNumber(options.round, "round"),
            includeZero: options.includeZero ?? null,
          },
        },
        { signal: options.signal },
      ),
    );
  }

  /** State and progress of a scan. */
  holderScan(id: string, signal?: AbortSignal): Promise<ScanResponse> {
    return this.call(() => this.scans.getHolderScan({ chain: this.chain, id }, { signal }));
  }

  /** A result page of a scan (readable while it runs). */
  holderScanPage(id: string, options: PageOptions = {}): Promise<ScanHoldersResponse> {
    return this.call(() => this.scans.getHolderScanResult({ chain: this.chain, id, cursor: options.cursor, limit: checkLimit(options.limit) }, { signal: options.signal }));
  }

  /** Cancels a scan. */
  cancelHolderScan(id: string, signal?: AbortSignal): Promise<ScanResponse> {
    return this.call(() => this.scans.cancelHolderScan({ chain: this.chain, id }, { signal }));
  }

  /** Every holding of an account, page by page. */
  async *iterateAccountTokens(address: string, options: AccountTokensOptions = {}): AsyncGenerator<HoldingDto> {
    let cursor = options.cursor;
    do {
      const page = await this.accountTokens(address, { ...options, cursor });
      yield* page.items;
      cursor = page.nextCursor ?? undefined;
    } while (cursor);
  }

  /** Every effect of an account and token, newest first. */
  async *iterateHistory(address: string, kind: TokenKind, tokenId: Id, options: HistoryOptions = {}): AsyncGenerator<EffectDto> {
    let cursor = options.cursor;
    do {
      const page = await this.history(address, kind, tokenId, { ...options, cursor });
      yield* page.items;
      cursor = page.nextCursor ?? undefined;
    } while (cursor);
  }

  /**
   * Every holder of a token at one round. Without `round` the latest published round of the first page is pinned for all
   * following pages, so the walk is consistent even while the indexer advances. Very large tokens at a past round can time out
   * (HTTP 503); use {@link exportHolders} for those.
   */
  async *iterateHolders(kind: TokenKind, tokenId: Id, options: HoldersOptions = {}): AsyncGenerator<HolderDto> {
    let cursor = options.cursor;
    let round = options.round;
    do {
      const page = await this.holders(kind, tokenId, { ...options, round, cursor });
      round ??= page.round;
      yield* page.items;
      cursor = page.nextCursor ?? undefined;
    } while (cursor);
  }

  /**
   * Exports every holder of a token through an asynchronous scan: starts it, waits for it and streams the result. Works for tokens
   * of any size. Throws {@link ScanError} if the scan fails or is cancelled. Stopping early (break, error, abort) cancels the scan on the server.
   */
  async *exportHolders(kind: TokenKind, tokenId: Id, options: ExportHoldersOptions = {}): AsyncGenerator<HolderDto> {
    checkLimit(options.limit); // before the scan exists
    if (options.pollIntervalMs !== undefined && !(options.pollIntervalMs > 0 && Number.isFinite(options.pollIntervalMs))) {
      throw new RangeError(`pollIntervalMs must be a positive number (got ${String(options.pollIntervalMs)})`);
    }
    const started = await this.startHolderScan(kind, tokenId, { round: options.round, includeZero: options.includeZero, signal: options.signal });
    const poll = options.pollIntervalMs ?? 1000;
    let cursor: string | undefined;
    let finished = false;
    try {
      for (;;) {
        const page = await this.holderScanPage(started.id, { cursor, limit: options.limit, signal: options.signal });
        // A finished scan has nothing left to release, whatever the consumer does with the last rows.
        finished = page.complete;
        yield* page.items;
        if (page.complete) {
          return;
        }
        if (page.nextCursor) {
          cursor = page.nextCursor;
          if (page.items.length > 0) {
            continue; // more rows may be waiting right now
          }
        }
        // Caught up with a running scan (an empty page), or no row exists yet: look at the scan, then wait. The cursor is kept.
        const scan = await this.holderScan(started.id, options.signal);
        if (TERMINAL_SCAN_STATES.has(scan.state) && scan.state !== "Completed") {
          finished = true;
          throw new ScanError(`Holder scan ${started.id} ended as ${scan.state}${scan.error ? `: ${scan.error}` : ""}`, scan);
        }
        if (scan.state === "Completed" && page.items.length > 0 && !page.nextCursor) {
          // The scan finished between two reads of the server: this was its last page, every row has been delivered.
          finished = true;
          return;
        }
        await sleep(poll, options.signal);
      }
    } finally {
      if (!finished) {
        // Stopped early (break, error, abort): release the server-side scan, which counts against the server's limit of active scans.
        await this.cancelHolderScan(started.id, AbortSignal.timeout(5000)).catch(() => undefined);
      }
    }
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  let body = "";
  try {
    body = await response.text();
  } catch {
    // The body is optional.
  }
  let message = response.statusText || `HTTP ${response.status}`;
  try {
    const parsed = JSON.parse(body) as { error?: unknown };
    if (typeof parsed.error === "string") {
      message = parsed.error;
    }
  } catch {
    // Not JSON (for example a 504 from a proxy).
  }
  return new ApiError(response.status, message, parseRetryAfter(response.headers.get("Retry-After")), body);
}

/** `Retry-After` is a number of seconds or an HTTP date; anything else (or an absurd value) is ignored. */
function parseRetryAfter(value: string | null): number | undefined {
  if (!value) {
    return undefined;
  }
  const seconds = /^\s*\d+(\.\d+)?\s*$/.test(value) ? Number(value) : (Date.parse(value) - Date.now()) / 1000;
  return Number.isFinite(seconds) && seconds > 0 && seconds < 1e9 ? seconds : undefined;
}

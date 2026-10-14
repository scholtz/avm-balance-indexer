//! Rust SDK for the AVM Balance Indexer.
//!
//! Token balances, holders and history for Algorand MainNet, Algorand TestNet and Voi. The request and response types are generated
//! from the OpenAPI document of the service (`openapi.json`); this crate adds the public deployments as defaults, custom hosts,
//! pagination and typed errors.
//!
//! ```no_run
//! use avm_balance_indexer::{AvmBalanceIndexer, ChainId, TokenKind};
//!
//! # async fn run() -> Result<(), avm_balance_indexer::Error> {
//! let indexer = AvmBalanceIndexer::new(ChainId::Voi)?; // the public Voi deployment
//! let status = indexer.status().await?;
//! println!("indexed up to round {:?}", status.indexed_round);
//!
//! let balance = indexer.balance("ADDRESS...", TokenKind::Algo, 0, None).await?;
//! println!("{} microAlgo", balance.amount);
//! # Ok(()) }
//! ```

#[allow(clippy::all, unused_imports, dead_code, missing_docs)]
mod generated;

use std::time::Duration;

use futures_core::Stream;
use futures_util::stream;
use progenitor_client::ResponseValue;

pub use generated::types::{
    BalanceResponse, ChainId, EffectDto, ErrorResponse, HistoryResponse, HolderDto, HoldersResponse, HoldingDto, HoldingsResponse,
    ScanHoldersResponse, ScanRequest, ScanResponse, StatusResponse, TokenKind, TokenRef, TokenResponse,
};

/// Public deployments, used when no base URL is given.
pub fn default_base_url(chain: ChainId) -> &'static str {
    match chain {
        ChainId::AlgorandMainnet => "https://mainnet.balance-indexer.biatec.io",
        ChainId::AlgorandTestnet => "https://testnet.balance-indexer.biatec.io",
        ChainId::Voi => "https://voi.balance-indexer.biatec.io",
    }
}

/// What can go wrong.
#[derive(Debug)]
pub enum Error {
    /// The server answered with an error status.
    Api {
        /// HTTP status code.
        status: u16,
        /// The `error` field of the response, or the status text.
        message: String,
        /// Seconds to wait before retrying, from `Retry-After` (429 and 503).
        retry_after: Option<Duration>,
        /// Raw response body when it was not the usual JSON error.
        body: Option<String>,
    },
    /// The request never produced a response (network failure or timeout).
    Connection(String),
    /// The response could not be read as the documented type (for example an HTML error page of a proxy; its status is not reported).
    Decode(String),
    /// A holder scan ended as `Failed` or `Cancelled` before it finished.
    Scan(Box<ScanResponse>),
    /// An argument is invalid; nothing was sent.
    InvalidArgument(String),
}

impl Error {
    /// Unknown token, round beyond the indexed one, or wrong chain.
    pub fn is_not_found(&self) -> bool {
        matches!(self, Error::Api { status: 404, .. })
    }

    /// Rate limited or nothing indexed yet: retry after `retry_after`.
    pub fn is_retryable(&self) -> bool {
        matches!(self, Error::Api { status: 429 | 503 | 504, .. })
    }
}

impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Error::Api { status, message, .. } => write!(f, "HTTP {status}: {message}"),
            Error::Connection(message) => write!(f, "connection failed: {message}"),
            Error::Decode(message) => write!(f, "unexpected response: {message}"),
            Error::Scan(scan) => write!(
                f,
                "holder scan {} ended as {}{}",
                scan.id,
                scan.state,
                scan.error.as_deref().map(|e| format!(": {e}")).unwrap_or_default()
            ),
            Error::InvalidArgument(message) => write!(f, "invalid argument: {message}"),
        }
    }
}

impl std::error::Error for Error {}

/// A specialised `Result` for this crate.
pub type Result<T> = std::result::Result<T, Error>;

async fn convert(error: progenitor_client::Error<ErrorResponse>) -> Error {
    use progenitor_client::Error as E;
    match error {
        E::ErrorResponse(response) => {
            let status = response.status().as_u16();
            let retry_after = retry_after(response.headers());
            Error::Api { status, message: response.into_inner().error, retry_after, body: None }
        }
        E::UnexpectedResponse(response) => {
            let status = response.status();
            let retry_after = retry_after(response.headers());
            let body = response.text().await.unwrap_or_default();
            let message = status.canonical_reason().map(str::to_owned).unwrap_or_else(|| format!("HTTP {}", status.as_u16()));
            Error::Api { status: status.as_u16(), message, retry_after, body: Some(body) }
        }
        E::InvalidRequest(message) => Error::InvalidArgument(message),
        E::InvalidResponsePayload(_, error) => Error::Decode(error.to_string()),
        E::CommunicationError(error) => Error::Connection(error.to_string()),
        other => Error::Connection(other.to_string()),
    }
}

fn retry_after(headers: &reqwest::header::HeaderMap) -> Option<Duration> {
    let value = headers.get("retry-after")?.to_str().ok()?.trim();
    // A number of seconds or an HTTP date. "inf", NaN and absurd values from a misbehaving proxy are ignored rather than turned into a panic.
    let seconds = match value.parse::<f64>() {
        Ok(seconds) => seconds,
        Err(_) => httpdate::parse_http_date(value).ok()?.duration_since(std::time::SystemTime::now()).ok()?.as_secs_f64(),
    };
    (seconds > 0.0 && seconds < 1e9).then(|| Duration::from_secs_f64(seconds))
}

async fn unwrap<T>(result: std::result::Result<ResponseValue<T>, progenitor_client::Error<ErrorResponse>>) -> Result<T> {
    match result {
        Ok(value) => Ok(value.into_inner()),
        Err(error) => Err(convert(error).await),
    }
}

fn page_size(limit: Option<u32>) -> Result<Option<std::num::NonZeroU32>> {
    match limit {
        None => Ok(None),
        Some(value) if (1..=1000).contains(&value) => Ok(std::num::NonZeroU32::new(value)),
        Some(value) => Err(Error::InvalidArgument(format!("limit must be 1 to 1000 (got {value})"))),
    }
}

/// Options for [`AvmBalanceIndexer::account_tokens`].
#[derive(Debug, Clone, Default)]
pub struct AccountTokensOptions {
    /// Also list tokens the account is opted in to but holds none of.
    pub include_zero: Option<bool>,
    /// Cursor of the previous page (`next_cursor`).
    pub cursor: Option<String>,
    /// Page size, 1 to 1000 (server default 100).
    pub limit: Option<u32>,
}

/// Options for [`AvmBalanceIndexer::history`].
#[derive(Debug, Clone, Default)]
pub struct HistoryOptions {
    /// Oldest round to include.
    pub from_round: Option<u64>,
    /// Newest round to include.
    pub to_round: Option<u64>,
    /// Cursor of the previous page (`next_cursor`).
    pub cursor: Option<String>,
    /// Page size, 1 to 1000 (server default 100).
    pub limit: Option<u32>,
}

/// Options for [`AvmBalanceIndexer::holders`].
#[derive(Debug, Clone, Default)]
pub struct HoldersOptions {
    /// End-of-round state to read; `None` is the latest published round.
    pub round: Option<u64>,
    /// Also list opted-in accounts that hold zero.
    pub include_zero: Option<bool>,
    /// Cursor of the previous page (`next_cursor`).
    pub cursor: Option<String>,
    /// Page size, 1 to 1000 (server default 100).
    pub limit: Option<u32>,
}

/// Options for [`AvmBalanceIndexer::export_holders`].
#[derive(Debug, Clone, Default)]
pub struct ExportOptions {
    /// Round to read; `None` is the latest published round.
    pub round: Option<u64>,
    /// Also list opted-in accounts that hold zero.
    pub include_zero: Option<bool>,
    /// Time between polls while the scan runs. Default one second.
    pub poll_interval: Option<Duration>,
    /// Page size when reading the result.
    pub limit: Option<u32>,
}

/// Builds an [`AvmBalanceIndexer`].
#[derive(Debug, Clone)]
pub struct AvmBalanceIndexerBuilder {
    chain: ChainId,
    base_url: Option<String>,
    headers: Vec<(String, String)>,
    timeout: Option<Duration>,
    http_client: Option<reqwest::Client>,
}

impl AvmBalanceIndexerBuilder {
    /// Root URL of a deployment, without `/v1/...`. Default: the public deployment of the chain.
    pub fn base_url(mut self, base_url: impl Into<String>) -> Self {
        self.base_url = Some(base_url.into());
        self
    }

    /// Adds a header to every request (for example an API key of your own gateway).
    pub fn header(mut self, name: impl Into<String>, value: impl Into<String>) -> Self {
        self.headers.push((name.into(), value.into()));
        self
    }

    /// Timeout of each request of the default HTTP client. Default 30 seconds.
    pub fn timeout(mut self, timeout: Duration) -> Self {
        self.timeout = Some(timeout);
        self
    }

    /// Uses your own `reqwest::Client` (proxy, TLS settings). `header` and `timeout` then have no effect.
    pub fn http_client(mut self, client: reqwest::Client) -> Self {
        self.http_client = Some(client);
        self
    }

    /// Creates the client.
    pub fn build(self) -> Result<AvmBalanceIndexer> {
        let base_url = self.base_url.unwrap_or_else(|| default_base_url(self.chain).to_owned()).trim_end_matches('/').to_owned();
        let client = match self.http_client {
            Some(client) => client,
            None => {
                let mut headers = reqwest::header::HeaderMap::new();
                for (name, value) in &self.headers {
                    let name = reqwest::header::HeaderName::from_bytes(name.as_bytes())
                        .map_err(|e| Error::InvalidArgument(format!("header name {name:?}: {e}")))?;
                    let value = reqwest::header::HeaderValue::from_str(value).map_err(|e| Error::InvalidArgument(format!("header value: {e}")))?;
                    headers.insert(name, value);
                }
                reqwest::Client::builder()
                    .user_agent("avm-balance-indexer-rust-sdk")
                    .default_headers(headers)
                    .timeout(self.timeout.unwrap_or(Duration::from_secs(30)))
                    .build()
                    .map_err(|e| Error::Connection(e.to_string()))?
            }
        };
        Ok(AvmBalanceIndexer { chain: self.chain, api: generated::Client::new_with_client(&base_url, client), base_url })
    }
}

/// Client for one chain of an AVM Balance Indexer deployment.
#[derive(Debug, Clone)]
pub struct AvmBalanceIndexer {
    chain: ChainId,
    base_url: String,
    api: generated::Client,
}

impl AvmBalanceIndexer {
    /// A client for the public deployment of `chain`.
    pub fn new(chain: ChainId) -> Result<Self> {
        Self::builder(chain).build()
    }

    /// A client with custom settings.
    pub fn builder(chain: ChainId) -> AvmBalanceIndexerBuilder {
        AvmBalanceIndexerBuilder { chain, base_url: None, headers: Vec::new(), timeout: None, http_client: None }
    }

    /// The chain this client reads.
    pub fn chain(&self) -> ChainId {
        self.chain
    }

    /// Root URL of the deployment in use.
    pub fn base_url(&self) -> &str {
        &self.base_url
    }

    /// Indexed round, target round, lag and ingestion state.
    pub async fn status(&self) -> Result<StatusResponse> {
        unwrap(self.api.get_chain_status().chain(self.chain).send().await).await
    }

    /// Current holdings of an account (one page).
    pub async fn account_tokens(&self, address: &str, options: AccountTokensOptions) -> Result<HoldingsResponse> {
        let mut request = self.api.get_account_tokens().chain(self.chain).address(address);
        if let Some(value) = options.include_zero {
            request = request.include_zero(value);
        }
        if let Some(value) = options.cursor {
            request = request.cursor(value);
        }
        if let Some(value) = page_size(options.limit)? {
            request = request.limit(value);
        }
        unwrap(request.send().await).await
    }

    /// End-of-round balance of an account and token; `round` `None` is the latest published round. `amount` is a decimal string.
    pub async fn balance(&self, address: &str, kind: TokenKind, token_id: u64, round: Option<u64>) -> Result<BalanceResponse> {
        let mut request = self.api.get_token_balance().chain(self.chain).address(address).kind(kind).token_id(token_id);
        if let Some(value) = round {
            request = request.round(value);
        }
        unwrap(request.send().await).await
    }

    /// Balance effects of an account and token, newest first (one page).
    pub async fn history(&self, address: &str, kind: TokenKind, token_id: u64, options: HistoryOptions) -> Result<HistoryResponse> {
        let mut request = self.api.get_token_history().chain(self.chain).address(address).kind(kind).token_id(token_id);
        if let Some(value) = options.from_round {
            request = request.from_round(value);
        }
        if let Some(value) = options.to_round {
            request = request.to_round(value);
        }
        if let Some(value) = options.cursor {
            request = request.cursor(value);
        }
        if let Some(value) = page_size(options.limit)? {
            request = request.limit(value);
        }
        unwrap(request.send().await).await
    }

    /// Holders of a token at the end of a round (one page, in account order).
    pub async fn holders(&self, kind: TokenKind, token_id: u64, options: HoldersOptions) -> Result<HoldersResponse> {
        let mut request = self.api.get_token_holders().chain(self.chain).kind(kind).token_id(token_id);
        if let Some(value) = options.round {
            request = request.round(value);
        }
        if let Some(value) = options.include_zero {
            request = request.include_zero(value);
        }
        if let Some(value) = options.cursor {
            request = request.cursor(value);
        }
        if let Some(value) = page_size(options.limit)? {
            request = request.limit(value);
        }
        unwrap(request.send().await).await
    }

    /// Token metadata (symbol, name, decimals, standard).
    pub async fn token(&self, kind: TokenKind, token_id: u64) -> Result<TokenResponse> {
        unwrap(self.api.get_token().chain(self.chain).kind(kind).token_id(token_id).send().await).await
    }

    /// Starts an asynchronous export of every holder of a token. Prefer [`export_holders_stream`](Self::export_holders_stream).
    pub async fn start_holder_scan(&self, kind: TokenKind, token_id: u64, request: ScanRequest) -> Result<ScanResponse> {
        unwrap(self.api.create_holder_scan().chain(self.chain).kind(kind).token_id(token_id).body(request).send().await).await
    }

    /// State and progress of a scan.
    pub async fn holder_scan(&self, id: &str) -> Result<ScanResponse> {
        unwrap(self.api.get_holder_scan().chain(self.chain).id(id).send().await).await
    }

    /// A result page of a scan (readable while it runs).
    pub async fn holder_scan_page(&self, id: &str, cursor: Option<String>, limit: Option<u32>) -> Result<ScanHoldersResponse> {
        let mut request = self.api.get_holder_scan_result().chain(self.chain).id(id);
        if let Some(value) = cursor {
            request = request.cursor(value);
        }
        if let Some(value) = page_size(limit)? {
            request = request.limit(value);
        }
        unwrap(request.send().await).await
    }

    /// Cancels a scan.
    pub async fn cancel_holder_scan(&self, id: &str) -> Result<ScanResponse> {
        unwrap(self.api.cancel_holder_scan().chain(self.chain).id(id).send().await).await
    }

    /// Every holding of an account, page by page.
    pub fn account_tokens_stream(&self, address: &str, options: AccountTokensOptions) -> impl Stream<Item = Result<HoldingDto>> + '_ {
        let address = address.to_owned();
        paginate(options.cursor.clone(), move |cursor| {
            let address = address.clone();
            let mut page_options = options.clone();
            async move {
                page_options.cursor = cursor;
                let page = self.account_tokens(&address, page_options).await?;
                Ok((page.items, page.next_cursor))
            }
        })
    }

    /// Every effect of an account and token, newest first.
    pub fn history_stream(&self, address: &str, kind: TokenKind, token_id: u64, options: HistoryOptions) -> impl Stream<Item = Result<EffectDto>> + '_ {
        let address = address.to_owned();
        paginate(options.cursor.clone(), move |cursor| {
            let address = address.clone();
            let mut page_options = options.clone();
            async move {
                page_options.cursor = cursor;
                let page = self.history(&address, kind, token_id, page_options).await?;
                Ok((page.items, page.next_cursor))
            }
        })
    }

    /// Every holder of a token at one round. Without `round` the round of the first page is pinned for the following pages, so the
    /// walk stays consistent while the indexer advances. A very large token at a past round can time out (HTTP 503): use
    /// [`export_holders_stream`](Self::export_holders_stream) for those.
    pub fn holders_stream(&self, kind: TokenKind, token_id: u64, options: HoldersOptions) -> impl Stream<Item = Result<HolderDto>> + '_ {
        let pinned = std::sync::Arc::new(std::sync::Mutex::new(options.round));
        paginate(options.cursor.clone(), move |cursor| {
            let pinned = pinned.clone();
            let mut page_options = options.clone();
            async move {
                page_options.cursor = cursor;
                page_options.round = *pinned.lock().expect("round lock");
                let page = self.holders(kind, token_id, page_options).await?;
                pinned.lock().expect("round lock").get_or_insert(page.round);
                Ok((page.items, page.next_cursor))
            }
        })
    }

    /// Exports every holder of a token through an asynchronous scan: the stream starts the scan, waits for it and yields the holders as the
    /// pages become available. Works for tokens of any size without holding them all in memory. The stream ends with [`Error::Scan`] if
    /// the scan fails or is cancelled. Collect it with `try_collect` if you do want a `Vec`. Dropping the stream before it ends cancels the
    /// scan on the server (best effort, from inside a Tokio runtime).
    pub fn export_holders_stream(&self, kind: TokenKind, token_id: u64, options: ExportOptions) -> impl Stream<Item = Result<HolderDto>> + '_ {
        enum Phase {
            Start,
            // `look` is true when the previous page had nothing new: check the scan and wait before reading again.
            Reading { guard: ScanGuard, cursor: Option<String>, look: bool, last_page_without_cursor: bool },
            Done,
        }
        let wait = options.poll_interval.unwrap_or(Duration::from_secs(1));
        let pages = stream::try_unfold(Phase::Start, move |phase| {
            let options = options.clone();
            async move {
                match phase {
                    Phase::Done => Ok(None),
                    Phase::Start => {
                        page_size(options.limit)?; // before the scan exists
                        if options.poll_interval.is_some_and(|interval| interval.is_zero()) {
                            return Err(Error::InvalidArgument("poll_interval must be positive".to_owned()));
                        }
                        let started = self
                            .start_holder_scan(kind, token_id, ScanRequest { include_zero: options.include_zero, round: options.round })
                            .await?;
                        let guard = ScanGuard { client: self.clone(), id: started.id.to_string(), armed: true };
                        Ok(Some((Vec::new(), Phase::Reading { guard, cursor: None, look: false, last_page_without_cursor: false })))
                    }
                    Phase::Reading { mut guard, cursor, look, last_page_without_cursor } => {
                        if look {
                            let scan = self.holder_scan(&guard.id).await?;
                            if scan.state == "Failed" || scan.state == "Cancelled" {
                                guard.armed = false;
                                return Err(Error::Scan(Box::new(scan)));
                            }
                            if scan.state == "Completed" && last_page_without_cursor {
                                // The scan finished between two reads of the server: the previous page was its last, every row has been delivered.
                                guard.armed = false;
                                return Ok(Some((Vec::new(), Phase::Done)));
                            }
                            tokio::time::sleep(wait).await;
                        }
                        let page = self.holder_scan_page(&guard.id, cursor.clone(), options.limit).await?;
                        if page.complete {
                            guard.armed = false;
                            return Ok(Some((page.items, Phase::Done)));
                        }
                        // An empty page (caught up with a running scan) or no cursor to continue from (for example the last page of a scan that
                        // failed): look at the scan and wait before the next read. The cursor of the previous page is kept.
                        let look = page.items.is_empty() || page.next_cursor.is_none();
                        let last_page_without_cursor = !page.items.is_empty() && page.next_cursor.is_none();
                        let cursor = page.next_cursor.or(cursor);
                        Ok(Some((page.items, Phase::Reading { guard, cursor, look, last_page_without_cursor })))
                    }
                }
            }
        });
        futures_util::TryStreamExt::try_flatten(futures_util::TryStreamExt::map_ok(pages, |items| stream::iter(items.into_iter().map(Ok))))
    }
}

/// Cancels the scan of an export that is dropped before it finished (best effort, inside a Tokio runtime): a scan counts against the
/// server's limit of active scans until it ends or expires.
struct ScanGuard {
    client: AvmBalanceIndexer,
    id: String,
    armed: bool,
}

impl Drop for ScanGuard {
    fn drop(&mut self) {
        if self.armed {
            if let Ok(runtime) = tokio::runtime::Handle::try_current() {
                let client = self.client.clone();
                let id = std::mem::take(&mut self.id);
                runtime.spawn(async move {
                    let _ = client.cancel_holder_scan(&id).await;
                });
            }
        }
    }
}

/// Turns a "fetch the page after this cursor" function into a stream of items.
fn paginate<T, F, Fut>(first_cursor: Option<String>, fetch: F) -> impl Stream<Item = Result<T>>
where
    F: Fn(Option<String>) -> Fut,
    Fut: std::future::Future<Output = Result<(Vec<T>, Option<String>)>>,
{
    enum State {
        Start(Option<String>),
        Next(String),
        Done,
    }
    let pages = stream::try_unfold(State::Start(first_cursor), move |state| {
        let cursor = match &state {
            State::Start(cursor) => cursor.clone(),
            State::Next(cursor) => Some(cursor.clone()),
            State::Done => return futures_util::future::Either::Left(std::future::ready(Ok(None))),
        };
        let page = fetch(cursor);
        futures_util::future::Either::Right(async move {
            let (items, next) = page.await?;
            let state = match next {
                Some(cursor) if !cursor.is_empty() => State::Next(cursor),
                _ => State::Done,
            };
            Ok(Some((stream::iter(items.into_iter().map(Ok)), state)))
        })
    });
    futures_util::TryStreamExt::try_flatten(pages)
}

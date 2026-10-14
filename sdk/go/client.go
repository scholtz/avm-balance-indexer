package avmbalanceindexer

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"net/http"
	"strconv"
	"strings"
	"time"
)

// DefaultBaseURLs are the public deployments, used when WithBaseURL is not given.
var DefaultBaseURLs = map[ChainId]string{
	AlgorandMainnet: "https://mainnet.balance-indexer.biatec.io",
	AlgorandTestnet: "https://testnet.balance-indexer.biatec.io",
	Voi:             "https://voi.balance-indexer.biatec.io",
}

// HTTPDoer is the part of *http.Client the SDK needs.
type HTTPDoer interface {
	Do(req *http.Request) (*http.Response, error)
}

// Client reads one chain of an AVM Balance Indexer deployment.
//
//	indexer, err := avmbalanceindexer.New(avmbalanceindexer.Voi, nil)                  // public Voi deployment
//	custom, err := avmbalanceindexer.New(avmbalanceindexer.AlgorandMainnet,
//	    &avmbalanceindexer.Options{BaseURL: "https://indexer.example.com"})             // your own deployment
type Client struct {
	chain   ChainId
	baseURL string
	api     *GeneratedClient
}

// Options configure New. The zero value (or nil) uses the public deployment of the chain.
type Options struct {
	// BaseURL is the root URL of a deployment, without /v1/... Default: the public deployment of the chain.
	BaseURL string
	// HTTPClient is your own HTTP client (proxy, transport, tracing). Timeout then has no effect.
	HTTPClient HTTPDoer
	// Headers are added to every request (for example an API key of your own gateway).
	Headers map[string]string
	// Timeout of each request of the default HTTP client. Default 30 seconds; negative disables it.
	Timeout time.Duration
}

// New returns a client for chain: AlgorandMainnet, AlgorandTestnet or Voi. options may be nil.
func New(chain ChainId, options *Options) (*Client, error) {
	defaultURL, ok := DefaultBaseURLs[chain]
	if !ok {
		return nil, fmt.Errorf("avmbalanceindexer: unknown chain %q (use AlgorandMainnet, AlgorandTestnet or Voi)", chain)
	}
	if options == nil {
		options = &Options{}
	}
	baseURL := strings.TrimRight(options.BaseURL, "/")
	if baseURL == "" {
		baseURL = defaultURL
	}
	var doer HTTPDoer = options.HTTPClient
	if doer == nil {
		timeout := options.Timeout
		switch {
		case timeout == 0:
			timeout = 30 * time.Second
		case timeout < 0:
			timeout = 0
		}
		doer = &http.Client{Timeout: timeout}
	}
	headers := map[string]string{"User-Agent": "avm-balance-indexer-go-sdk"}
	for name, value := range options.Headers {
		headers[name] = value
	}
	api, err := NewClient(baseURL, WithHTTPClient(doer), WithRequestEditorFn(func(_ context.Context, request *http.Request) error {
		for name, value := range headers {
			request.Header.Set(name, value)
		}
		return nil
	}))
	if err != nil {
		return nil, fmt.Errorf("avmbalanceindexer: invalid base URL %q: %w", baseURL, err)
	}
	return &Client{chain: chain, baseURL: baseURL, api: api}, nil
}

// Chain is the chain this client reads.
func (c *Client) Chain() ChainId { return c.chain }

// BaseURL is the root URL of the deployment in use.
func (c *Client) BaseURL() string { return c.baseURL }

// APIError is returned when the server answers with an error status.
type APIError struct {
	// Status is the HTTP status code.
	Status int
	// Message is the "error" field of the response, or the status text.
	Message string
	// RetryAfter is how long to wait before retrying (429 and 503), zero when the server did not say.
	RetryAfter time.Duration
	// Body is the raw response body.
	Body []byte
}

func (e *APIError) Error() string {
	return fmt.Sprintf("avmbalanceindexer: HTTP %d: %s", e.Status, e.Message)
}

// IsNotFound reports an unknown token, a round beyond the indexed one, or a wrong chain.
func (e *APIError) IsNotFound() bool { return e.Status == http.StatusNotFound }

// IsRetryable reports a rate limit or an indexer that has nothing yet: retry after RetryAfter.
func (e *APIError) IsRetryable() bool {
	return e.Status == http.StatusTooManyRequests || e.Status == http.StatusServiceUnavailable || e.Status == http.StatusGatewayTimeout
}

// ScanError is returned by ExportHolders when the scan ended as Failed or Cancelled.
type ScanError struct {
	Scan *ScanResponse
}

func (e *ScanError) Error() string {
	message := fmt.Sprintf("avmbalanceindexer: holder scan %s ended as %s", e.Scan.Id, e.Scan.State)
	if e.Scan.Error != nil && *e.Scan.Error != "" {
		message += ": " + *e.Scan.Error
	}
	return message
}

// IsNotFound reports whether err is an APIError for a missing thing.
func IsNotFound(err error) bool {
	var apiError *APIError
	return errors.As(err, &apiError) && apiError.IsNotFound()
}

// ErrInvalidArgument is wrapped by the errors for arguments the SDK refuses before sending anything.
var ErrInvalidArgument = errors.New("avmbalanceindexer: invalid argument")

func checkLimit(limit *int32) error {
	if limit != nil && (*limit < 1 || *limit > 1000) {
		return fmt.Errorf("%w: limit must be 1 to 1000 (got %d)", ErrInvalidArgument, *limit)
	}
	return nil
}

// call runs one generated request and decodes the JSON answer.
func call[T any](do func() (*http.Response, error)) (*T, error) {
	response, err := do()
	if err != nil {
		return nil, fmt.Errorf("avmbalanceindexer: request failed: %w", err)
	}
	defer response.Body.Close()
	body, err := io.ReadAll(response.Body)
	if err != nil {
		return nil, fmt.Errorf("avmbalanceindexer: reading the response failed: %w", err)
	}
	if response.StatusCode < 200 || response.StatusCode > 299 {
		return nil, toAPIError(response, body)
	}
	var result T
	if err := json.Unmarshal(body, &result); err != nil {
		return nil, fmt.Errorf("avmbalanceindexer: unexpected response (HTTP %d): %w", response.StatusCode, err)
	}
	return &result, nil
}

func toAPIError(response *http.Response, body []byte) *APIError {
	message := http.StatusText(response.StatusCode)
	if message == "" {
		message = "HTTP " + strconv.Itoa(response.StatusCode)
	}
	var parsed ErrorResponse
	if json.Unmarshal(body, &parsed) == nil && parsed.Error != "" {
		message = parsed.Error
	}
	retryAfter := parseRetryAfter(response.Header.Get("Retry-After"))
	return &APIError{Status: response.StatusCode, Message: message, RetryAfter: retryAfter, Body: body}
}

// Status returns the indexed round, target round, lag and ingestion state.
func (c *Client) Status(ctx context.Context) (*StatusResponse, error) {
	return call[StatusResponse](func() (*http.Response, error) { return c.api.GetChainStatus(ctx, c.chain) })
}

// AccountTokens returns one page of the current holdings of an account. params may be nil.
func (c *Client) AccountTokens(ctx context.Context, address string, params *GetAccountTokensParams) (*HoldingsResponse, error) {
	if params != nil {
		if err := checkLimit(params.Limit); err != nil {
			return nil, err
		}
	}
	return call[HoldingsResponse](func() (*http.Response, error) {
		return c.api.GetAccountTokens(ctx, c.chain, address, params)
	})
}

// Balance returns the end-of-round balance of an account and token. round nil means the latest published round. Amount is a decimal string.
func (c *Client) Balance(ctx context.Context, address string, kind TokenKind, tokenID uint64, round *uint64) (*BalanceResponse, error) {
	return call[BalanceResponse](func() (*http.Response, error) {
		return c.api.GetTokenBalance(ctx, c.chain, address, kind, tokenID, &GetTokenBalanceParams{Round: round})
	})
}

// History returns one page of the balance effects of an account and token, newest first. params may be nil.
func (c *Client) History(ctx context.Context, address string, kind TokenKind, tokenID uint64, params *GetTokenHistoryParams) (*HistoryResponse, error) {
	if params != nil {
		if err := checkLimit(params.Limit); err != nil {
			return nil, err
		}
	}
	return call[HistoryResponse](func() (*http.Response, error) {
		return c.api.GetTokenHistory(ctx, c.chain, address, kind, tokenID, params)
	})
}

// Holders returns one page of the holders of a token at the end of a round, in account order. params may be nil.
func (c *Client) Holders(ctx context.Context, kind TokenKind, tokenID uint64, params *GetTokenHoldersParams) (*HoldersResponse, error) {
	if params != nil {
		if err := checkLimit(params.Limit); err != nil {
			return nil, err
		}
	}
	return call[HoldersResponse](func() (*http.Response, error) {
		return c.api.GetTokenHolders(ctx, c.chain, kind, tokenID, params)
	})
}

// Token returns the metadata of a token (symbol, name, decimals, standard).
func (c *Client) Token(ctx context.Context, kind TokenKind, tokenID uint64) (*TokenResponse, error) {
	return call[TokenResponse](func() (*http.Response, error) { return c.api.GetToken(ctx, c.chain, kind, tokenID) })
}

// StartHolderScan starts an asynchronous export of every holder of a token. Prefer ExportHolders. request may be nil.
func (c *Client) StartHolderScan(ctx context.Context, kind TokenKind, tokenID uint64, request *ScanRequest) (*ScanResponse, error) {
	if request == nil {
		request = &ScanRequest{}
	}
	return call[ScanResponse](func() (*http.Response, error) {
		return c.api.CreateHolderScan(ctx, c.chain, kind, tokenID, *request)
	})
}

// HolderScan returns the state and progress of a scan.
func (c *Client) HolderScan(ctx context.Context, id string) (*ScanResponse, error) {
	return call[ScanResponse](func() (*http.Response, error) { return c.api.GetHolderScan(ctx, c.chain, id) })
}

// HolderScanPage returns a result page of a scan; it can be read while the scan runs. params may be nil.
func (c *Client) HolderScanPage(ctx context.Context, id string, params *GetHolderScanResultParams) (*ScanHoldersResponse, error) {
	if params != nil {
		if err := checkLimit(params.Limit); err != nil {
			return nil, err
		}
	}
	return call[ScanHoldersResponse](func() (*http.Response, error) {
		return c.api.GetHolderScanResult(ctx, c.chain, id, params)
	})
}

// CancelHolderScan cancels a scan.
func (c *Client) CancelHolderScan(ctx context.Context, id string) (*ScanResponse, error) {
	return call[ScanResponse](func() (*http.Response, error) { return c.api.CancelHolderScan(ctx, c.chain, id) })
}

// ForEachAccountToken calls fn for every holding of an account, page by page. A non-nil error from fn stops the walk and is returned.
func (c *Client) ForEachAccountToken(ctx context.Context, address string, params *GetAccountTokensParams, fn func(HoldingDto) error) error {
	p := GetAccountTokensParams{}
	if params != nil {
		p = *params
	}
	for {
		page, err := c.AccountTokens(ctx, address, &p)
		if err != nil {
			return err
		}
		for _, item := range page.Items {
			if err := fn(item); err != nil {
				return err
			}
		}
		if page.NextCursor == nil || *page.NextCursor == "" {
			return nil
		}
		p.Cursor = page.NextCursor
	}
}

// ForEachHistory calls fn for every effect of an account and token, newest first.
func (c *Client) ForEachHistory(ctx context.Context, address string, kind TokenKind, tokenID uint64, params *GetTokenHistoryParams, fn func(EffectDto) error) error {
	p := GetTokenHistoryParams{}
	if params != nil {
		p = *params
	}
	for {
		page, err := c.History(ctx, address, kind, tokenID, &p)
		if err != nil {
			return err
		}
		for _, item := range page.Items {
			if err := fn(item); err != nil {
				return err
			}
		}
		if page.NextCursor == nil || *page.NextCursor == "" {
			return nil
		}
		p.Cursor = page.NextCursor
	}
}

// ForEachHolder calls fn for every holder of a token at one round. Without params.Round the round of the first page is pinned for the
// following pages, so the walk stays consistent while the indexer advances. A very large token at a past round can time out (HTTP 503):
// use ExportHolders for those.
func (c *Client) ForEachHolder(ctx context.Context, kind TokenKind, tokenID uint64, params *GetTokenHoldersParams, fn func(HolderDto) error) error {
	p := GetTokenHoldersParams{}
	if params != nil {
		p = *params
	}
	for {
		page, err := c.Holders(ctx, kind, tokenID, &p)
		if err != nil {
			return err
		}
		if p.Round == nil {
			round := page.Round
			p.Round = &round
		}
		for _, item := range page.Items {
			if err := fn(item); err != nil {
				return err
			}
		}
		if page.NextCursor == nil || *page.NextCursor == "" {
			return nil
		}
		p.Cursor = page.NextCursor
	}
}

// ExportOptions tune ExportHolders. The zero value is fine.
type ExportOptions struct {
	// Round to read; nil means the latest published round.
	Round *uint64
	// IncludeZero also lists opted-in accounts that hold zero.
	IncludeZero *bool
	// PollInterval between polls while the scan runs. Default 1 second.
	PollInterval time.Duration
	// Limit is the page size when reading the result.
	Limit *int32
}

// ExportHolders exports every holder of a token through an asynchronous scan: it starts the scan, waits for it and calls fn for each
// holder. It works for tokens of any size and returns a *ScanError if the scan fails or is cancelled. Stopping early (fn returns an error, the context ends) cancels the scan on the server.
func (c *Client) ExportHolders(ctx context.Context, kind TokenKind, tokenID uint64, options ExportOptions, fn func(HolderDto) error) error {
	if err := checkLimit(options.Limit); err != nil { // before the scan exists
		return err
	}
	started, err := c.StartHolderScan(ctx, kind, tokenID, &ScanRequest{Round: options.Round, IncludeZero: options.IncludeZero})
	if err != nil {
		return err
	}
	id := started.Id.String()
	wait := options.PollInterval
	if wait <= 0 {
		wait = time.Second
	}
	finished := false
	defer func() {
		if !finished {
			// Stopped early (an error, a cancelled context): release the server-side scan, which counts against the server's limit of active scans.
			release, cancel := context.WithTimeout(context.WithoutCancel(ctx), 5*time.Second)
			defer cancel()
			_, _ = c.CancelHolderScan(release, id)
		}
	}()
	page := GetHolderScanResultParams{Limit: options.Limit}
	for {
		result, err := c.HolderScanPage(ctx, id, &page)
		if err != nil {
			return err
		}
		// A finished scan has nothing left to release, whatever fn does with the last rows.
		finished = result.Complete
		for _, item := range result.Items {
			if err := fn(item); err != nil {
				return err
			}
		}
		if result.Complete {
			return nil
		}
		if result.NextCursor != nil && *result.NextCursor != "" {
			page.Cursor = result.NextCursor
			if len(result.Items) > 0 {
				continue // more rows may be waiting right now
			}
		}
		// Caught up with a running scan (an empty page), or no row exists yet: look at the scan, then wait. The cursor is kept.
		scan, err := c.HolderScan(ctx, id)
		if err != nil {
			return err
		}
		if scan.State == "Failed" || scan.State == "Cancelled" {
			finished = true
			return &ScanError{Scan: scan}
		}
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-time.After(wait):
		}
	}
}

// parseRetryAfter reads a number of seconds or an HTTP date; anything else (or an absurd value) gives zero.
func parseRetryAfter(value string) time.Duration {
	value = strings.TrimSpace(value)
	if value == "" {
		return 0
	}
	seconds, err := strconv.ParseFloat(value, 64)
	if err != nil {
		date, dateErr := http.ParseTime(value)
		if dateErr != nil {
			return 0
		}
		seconds = time.Until(date).Seconds()
	}
	if math.IsNaN(seconds) || seconds <= 0 || seconds >= 1e9 {
		return 0
	}
	return time.Duration(seconds * float64(time.Second))
}

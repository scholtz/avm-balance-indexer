package avmbalanceindexer_test

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"net/url"
	"sync"
	"testing"
	"time"

	avm "github.com/scholtz/avm-balance-indexer/sdk/go"
)

type seen struct {
	Method string
	Path   string
	Query  url.Values
	Header http.Header
	Body   string
}

// fakeServer answers with whatever handler returns and records every request.
func fakeServer(t *testing.T, handler func(r *http.Request) (int, any, map[string]string)) (*httptest.Server, *[]seen) {
	t.Helper()
	var mu sync.Mutex
	requests := &[]seen{}
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		mu.Lock()
		*requests = append(*requests, seen{r.Method, r.URL.Path, r.URL.Query(), r.Header.Clone(), string(body)})
		mu.Unlock()
		status, payload, headers := handler(r)
		for name, value := range headers {
			w.Header().Set(name, value)
		}
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(status)
		if text, ok := payload.(string); ok {
			_, _ = w.Write([]byte(text))
			return
		}
		_ = json.NewEncoder(w).Encode(payload)
	}))
	t.Cleanup(server.Close)
	return server, requests
}

func holder(account string) map[string]any {
	return map[string]any{"account": account, "amount": "5", "optedIn": true, "lastChangedRound": 7}
}

func scan(state string, complete bool, message any) map[string]any {
	return map[string]any{
		"id": "11111111-1111-1111-1111-111111111111", "state": state, "token": map[string]any{"kind": "asa", "tokenId": 1},
		"round": 5, "includeZero": false, "holdersFound": 2, "complete": complete, "error": message,
		"createdAt": "2026-10-14T10:00:00Z", "updatedAt": "2026-10-14T10:00:00Z", "expiresAt": "2026-10-15T10:00:00Z",
	}
}

func TestDefaultsArePublicDeployments(t *testing.T) {
	cases := map[avm.ChainId]string{
		avm.AlgorandMainnet: "https://mainnet.balance-indexer.biatec.io",
		avm.AlgorandTestnet: "https://testnet.balance-indexer.biatec.io",
		avm.Voi:             "https://voi.balance-indexer.biatec.io",
	}
	for chain, want := range cases {
		client, err := avm.New(chain, nil)
		if err != nil {
			t.Fatal(err)
		}
		if client.BaseURL() != want || client.Chain() != chain {
			t.Errorf("%s: got %s, want %s", chain, client.BaseURL(), want)
		}
	}
	if len(avm.DefaultBaseURLs) != 3 {
		t.Errorf("expected 3 default deployments, got %d", len(avm.DefaultBaseURLs))
	}
}

func TestCustomBaseURLKeepsItsPrefix(t *testing.T) {
	server, requests := fakeServer(t, func(r *http.Request) (int, any, map[string]string) {
		return 200, map[string]any{"chain": "voi", "provider": "Cassandra", "state": "Following", "indexedRound": 10, "targetRound": 11, "lag": 1, "updatedAt": "2026-10-14T10:00:00Z"}, nil
	})
	client, err := avm.New(avm.Voi, &avm.Options{BaseURL: server.URL + "/indexer//", Headers: map[string]string{"X-Api-Key": "k"}})
	if err != nil {
		t.Fatal(err)
	}

	status, err := client.Status(context.Background())

	if err != nil {
		t.Fatal(err)
	}
	if status.IndexedRound == nil || *status.IndexedRound != 10 {
		t.Errorf("unexpected status %+v", status)
	}
	got := (*requests)[0]
	if got.Path != "/indexer/v1/voi/status" {
		t.Errorf("path %s", got.Path)
	}
	if got.Header.Get("X-Api-Key") != "k" || got.Header.Get("User-Agent") != "avm-balance-indexer-go-sdk" {
		t.Errorf("headers %v", got.Header)
	}
}

func TestUnknownChainIsRejected(t *testing.T) {
	if _, err := avm.New(avm.ChainId("solana"), nil); err == nil {
		t.Fatal("expected an error")
	}
}

func TestQueryParameters(t *testing.T) {
	server, requests := fakeServer(t, func(r *http.Request) (int, any, map[string]string) {
		return 200, map[string]any{"token": map[string]any{"kind": "asa", "tokenId": 31566704}, "round": 9, "indexedRound": 9, "items": []any{}, "nextCursor": nil}, nil
	})
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})
	round, includeZero, limit, cursor := uint64(9), true, int32(50), "abc"

	_, err := client.Holders(context.Background(), avm.Asa, 31566704, &avm.GetTokenHoldersParams{Round: &round, IncludeZero: &includeZero, Limit: &limit, Cursor: &cursor})

	if err != nil {
		t.Fatal(err)
	}
	got := (*requests)[0]
	if got.Path != "/v1/algorand-mainnet/tokens/asa/31566704/holders" {
		t.Errorf("path %s", got.Path)
	}
	want := map[string]string{"round": "9", "includeZero": "true", "limit": "50", "cursor": "abc"}
	for name, value := range want {
		if got.Query.Get(name) != value {
			t.Errorf("query %s = %v, want %s", name, got.Query[name], value)
		}
	}
}

func TestErrorStatusBecomesAPIError(t *testing.T) {
	server, _ := fakeServer(t, func(r *http.Request) (int, any, map[string]string) {
		return 429, map[string]any{"error": "slow down"}, map[string]string{"Retry-After": "7"}
	})
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})

	_, err := client.Status(context.Background())

	var apiError *avm.APIError
	if !errors.As(err, &apiError) {
		t.Fatalf("expected APIError, got %v", err)
	}
	if apiError.Status != 429 || apiError.Message != "slow down" || apiError.RetryAfter != 7*time.Second || !apiError.IsRetryable() {
		t.Errorf("unexpected %+v", apiError)
	}
}

func TestNotFoundAndPlainBody(t *testing.T) {
	server, _ := fakeServer(t, func(r *http.Request) (int, any, map[string]string) { return 404, "nope", nil })
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})

	_, err := client.Token(context.Background(), avm.Asa, 1)

	if !avm.IsNotFound(err) {
		t.Fatalf("expected not found, got %v", err)
	}
	var apiError *avm.APIError
	errors.As(err, &apiError)
	if string(apiError.Body) != "nope" {
		t.Errorf("body %q", apiError.Body)
	}
}

func TestConnectionFailureIsReturned(t *testing.T) {
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: "http://127.0.0.1:1", Timeout: 2 * time.Second})
	if _, err := client.Status(context.Background()); err == nil {
		t.Fatal("expected an error")
	}
}

func TestTimeout(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { time.Sleep(500 * time.Millisecond) }))
	t.Cleanup(server.Close)
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL, Timeout: 100 * time.Millisecond})
	if _, err := client.Status(context.Background()); err == nil {
		t.Fatal("expected a timeout")
	}
}

func TestForEachHolderPinsTheRound(t *testing.T) {
	server, requests := fakeServer(t, func(r *http.Request) (int, any, map[string]string) {
		page := func(account string, next any) map[string]any {
			return map[string]any{"token": map[string]any{"kind": "asa", "tokenId": 1}, "round": 42, "indexedRound": 50, "items": []any{holder(account)}, "nextCursor": next}
		}
		switch r.URL.Query().Get("cursor") {
		case "":
			return 200, page("A", "c1"), nil
		case "c1":
			return 200, page("B", "c2"), nil
		}
		return 200, page("C", nil), nil
	})
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})

	var accounts []string
	err := client.ForEachHolder(context.Background(), avm.Asa, 1, nil, func(h avm.HolderDto) error {
		accounts = append(accounts, h.Account)
		return nil
	})

	if err != nil {
		t.Fatal(err)
	}
	if len(accounts) != 3 || accounts[0] != "A" || accounts[2] != "C" {
		t.Errorf("accounts %v", accounts)
	}
	if _, has := (*requests)[0].Query["round"]; has {
		t.Error("the first page must not pass a round")
	}
	if (*requests)[1].Query.Get("round") != "42" || (*requests)[2].Query.Get("round") != "42" {
		t.Errorf("the round was not pinned: %v %v", (*requests)[1].Query, (*requests)[2].Query)
	}
}

func TestForEachStopsOnCallbackError(t *testing.T) {
	server, _ := fakeServer(t, func(r *http.Request) (int, any, map[string]string) {
		return 200, map[string]any{"token": map[string]any{"kind": "asa", "tokenId": 1}, "round": 42, "indexedRound": 50, "items": []any{holder("A"), holder("B")}, "nextCursor": "more"}, nil
	})
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})
	stop := errors.New("stop")

	count := 0
	err := client.ForEachHolder(context.Background(), avm.Asa, 1, nil, func(avm.HolderDto) error {
		count++
		return stop
	})

	if !errors.Is(err, stop) || count != 1 {
		t.Errorf("err %v, count %d", err, count)
	}
}

func TestExportHoldersWaitsForTheScanAndKeepsItsCursor(t *testing.T) {
	waited := false
	const id = "11111111-1111-1111-1111-111111111111"
	server, requests := fakeServer(t, func(r *http.Request) (int, any, map[string]string) {
		switch {
		case r.Method == http.MethodPost:
			return 202, scan("Pending", false, nil), nil
		case len(r.URL.Path) > 8 && r.URL.Path[len(r.URL.Path)-8:] == "/holders":
			if r.URL.Query().Get("cursor") == "" {
				return 200, map[string]any{"id": id, "state": "Running", "complete": false, "round": 5, "items": []any{holder("A")}, "nextCursor": "c1"}, nil
			}
			if !waited {
				// Caught up with a running scan: no new rows, and the server answers with the position to continue from.
				waited = true
				return 200, map[string]any{"id": id, "state": "Running", "complete": false, "round": 5, "items": []any{}, "nextCursor": "c1"}, nil
			}
			return 200, map[string]any{"id": id, "state": "Completed", "complete": true, "round": 5, "items": []any{holder("B")}, "nextCursor": nil}, nil
		}
		return 200, scan("Running", false, nil), nil
	})
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})

	var accounts []string
	err := client.ExportHolders(context.Background(), avm.Asa, 1, avm.ExportOptions{PollInterval: 10 * time.Millisecond}, func(h avm.HolderDto) error {
		accounts = append(accounts, h.Account)
		return nil
	})

	if err != nil {
		t.Fatal(err)
	}
	if len(accounts) != 2 || accounts[0] != "A" || accounts[1] != "B" {
		t.Errorf("accounts %v", accounts)
	}
	var cursors []string
	for _, request := range *requests {
		if len(request.Path) > 8 && request.Path[len(request.Path)-8:] == "/holders" {
			cursors = append(cursors, request.Query.Get("cursor"))
		}
	}
	if len(cursors) != 3 || cursors[0] != "" || cursors[1] != "c1" || cursors[2] != "c1" {
		t.Errorf("page cursors %q, want [\"\" c1 c1]", cursors)
	}
	// Caught up with a running scan: it looks at the scan and waits instead of asking for the next page at once.
	polled := false
	for _, request := range *requests {
		if request.Method == http.MethodGet && request.Path == "/v1/algorand-mainnet/scans/"+id {
			polled = true
		}
	}
	if !polled {
		t.Error("the scan state was never read while waiting")
	}
}

func TestStoppingAnExportEarlyCancelsTheScan(t *testing.T) {
	server, requests := fakeServer(t, func(r *http.Request) (int, any, map[string]string) {
		switch r.Method {
		case http.MethodPost:
			return 202, scan("Pending", false, nil), nil
		case http.MethodDelete:
			return 200, scan("Cancelled", false, nil), nil
		}
		return 200, map[string]any{"id": "11111111-1111-1111-1111-111111111111", "state": "Running", "complete": false, "round": 5, "items": []any{holder("A"), holder("B")}, "nextCursor": "c1"}, nil
	})
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})
	stop := errors.New("enough")

	err := client.ExportHolders(context.Background(), avm.Asa, 1, avm.ExportOptions{PollInterval: 10 * time.Millisecond}, func(avm.HolderDto) error { return stop })

	if !errors.Is(err, stop) {
		t.Fatalf("expected the callback error, got %v", err)
	}
	cancelled := false
	for _, request := range *requests {
		if request.Method == http.MethodDelete {
			cancelled = true
		}
	}
	if !cancelled {
		t.Error("the scan was not cancelled")
	}
}

func TestExportHoldersReportsAFailedScan(t *testing.T) {
	server, _ := fakeServer(t, func(r *http.Request) (int, any, map[string]string) {
		switch {
		case r.Method == http.MethodPost:
			return 202, scan("Failed", false, "boom"), nil
		case len(r.URL.Path) > 8 && r.URL.Path[len(r.URL.Path)-8:] == "/holders":
			return 200, map[string]any{"id": "11111111-1111-1111-1111-111111111111", "state": "Failed", "complete": false, "round": 5, "items": []any{}, "nextCursor": nil}, nil
		}
		return 200, scan("Failed", false, "boom"), nil
	})
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})

	err := client.ExportHolders(context.Background(), avm.Asa, 1, avm.ExportOptions{PollInterval: 10 * time.Millisecond}, func(avm.HolderDto) error { return nil })

	var scanError *avm.ScanError
	if !errors.As(err, &scanError) || scanError.Scan.State != "Failed" {
		t.Fatalf("expected ScanError, got %v", err)
	}
}

func TestStartHolderScanPostsNullDefaults(t *testing.T) {
	server, requests := fakeServer(t, func(r *http.Request) (int, any, map[string]string) { return 202, scan("Pending", false, nil), nil })
	client, _ := avm.New(avm.AlgorandMainnet, &avm.Options{BaseURL: server.URL})
	round := uint64(5)

	if _, err := client.StartHolderScan(context.Background(), avm.Asa, 1, &avm.ScanRequest{Round: &round}); err != nil {
		t.Fatal(err)
	}

	var body map[string]any
	_ = json.Unmarshal([]byte((*requests)[0].Body), &body)
	if (*requests)[0].Method != http.MethodPost || body["round"] != float64(5) {
		t.Errorf("unexpected request %+v", (*requests)[0])
	}
}

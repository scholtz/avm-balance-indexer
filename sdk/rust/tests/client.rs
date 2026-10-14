use std::time::Duration;

use avm_balance_indexer::{
    default_base_url, AccountTokensOptions, AvmBalanceIndexer, ChainId, Error, ExportOptions, HoldersOptions, ScanRequest, TokenKind,
};
use futures_util::TryStreamExt;
use serde_json::{json, Value};
use wiremock::matchers::{method, path, query_param, query_param_is_missing};
use wiremock::{Mock, MockServer, ResponseTemplate};

const SCAN_ID: &str = "11111111-1111-1111-1111-111111111111";

fn holder(account: &str) -> Value {
    json!({ "account": account, "amount": "5", "optedIn": true, "lastChangedRound": 7 })
}

fn holders_page(account: &str, next: Option<&str>) -> Value {
    json!({ "token": { "kind": "asa", "tokenId": 1 }, "round": 42, "indexedRound": 50, "items": [holder(account)], "nextCursor": next })
}

fn scan(state: &str, complete: bool, error: Option<&str>) -> Value {
    json!({
        "id": SCAN_ID, "state": state, "token": { "kind": "asa", "tokenId": 1 }, "round": 5, "includeZero": false,
        "holdersFound": 2, "complete": complete, "error": error,
        "createdAt": "2026-10-14T10:00:00Z", "updatedAt": "2026-10-14T10:00:00Z", "expiresAt": "2026-10-15T10:00:00Z"
    })
}

fn client(server: &MockServer, chain: ChainId) -> AvmBalanceIndexer {
    AvmBalanceIndexer::builder(chain).base_url(server.uri()).build().unwrap()
}

#[test]
fn defaults_are_the_public_deployments() {
    assert_eq!(default_base_url(ChainId::AlgorandMainnet), "https://mainnet.balance-indexer.biatec.io");
    assert_eq!(default_base_url(ChainId::AlgorandTestnet), "https://testnet.balance-indexer.biatec.io");
    assert_eq!(default_base_url(ChainId::Voi), "https://voi.balance-indexer.biatec.io");
    assert_eq!(AvmBalanceIndexer::new(ChainId::Voi).unwrap().base_url(), "https://voi.balance-indexer.biatec.io");
    assert_eq!(AvmBalanceIndexer::new(ChainId::AlgorandTestnet).unwrap().chain(), ChainId::AlgorandTestnet);
}

#[test]
fn a_custom_base_url_is_trimmed() {
    let indexer = AvmBalanceIndexer::builder(ChainId::Voi).base_url("https://my.example.com/indexer//").build().unwrap();
    assert_eq!(indexer.base_url(), "https://my.example.com/indexer");
}

#[test]
fn an_invalid_header_is_rejected_when_building() {
    let result = AvmBalanceIndexer::builder(ChainId::Voi).header("bad name", "x").build();
    assert!(matches!(result, Err(Error::InvalidArgument(_))));
}

#[tokio::test]
async fn the_chain_is_in_the_path_and_headers_are_sent() {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .and(path("/v1/voi/status"))
        .and(wiremock::matchers::header("X-Api-Key", "k"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "chain": "voi", "provider": "Cassandra", "state": "Following", "indexedRound": 10, "targetRound": 11, "lag": 1, "updatedAt": "2026-10-14T10:00:00Z"
        })))
        .expect(1)
        .mount(&server)
        .await;
    let indexer = AvmBalanceIndexer::builder(ChainId::Voi).base_url(server.uri()).header("X-Api-Key", "k").build().unwrap();

    let status = indexer.status().await.unwrap();

    assert_eq!(status.indexed_round, Some(10));
}

#[tokio::test]
async fn query_parameters_are_sent() {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .and(path("/v1/algorand-mainnet/tokens/asa/31566704/holders"))
        .and(query_param("round", "9"))
        .and(query_param("includeZero", "true"))
        .and(query_param("limit", "50"))
        .and(query_param("cursor", "abc"))
        .respond_with(ResponseTemplate::new(200).set_body_json(holders_page("A", None)))
        .expect(1)
        .mount(&server)
        .await;

    let page = client(&server, ChainId::AlgorandMainnet)
        .holders(TokenKind::Asa, 31566704, HoldersOptions { round: Some(9), include_zero: Some(true), limit: Some(50), cursor: Some("abc".into()) })
        .await
        .unwrap();

    assert_eq!(page.items.len(), 1);
}

#[tokio::test]
async fn an_invalid_limit_never_reaches_the_server() {
    let server = MockServer::start().await;
    let indexer = client(&server, ChainId::AlgorandMainnet);

    let result = indexer.account_tokens("ADDRESS", AccountTokensOptions { limit: Some(0), ..Default::default() }).await;

    assert!(matches!(result, Err(Error::InvalidArgument(_))));
    assert!(server.received_requests().await.unwrap().is_empty());
}

#[tokio::test]
async fn an_error_status_becomes_an_api_error_with_retry_after() {
    let server = MockServer::start().await;
    Mock::given(path("/v1/algorand-mainnet/status"))
        .respond_with(ResponseTemplate::new(429).insert_header("Retry-After", "7").set_body_json(json!({ "error": "slow down" })))
        .mount(&server)
        .await;

    let error = client(&server, ChainId::AlgorandMainnet).status().await.unwrap_err();

    match &error {
        Error::Api { status, message, retry_after, .. } => {
            assert_eq!(*status, 429);
            assert_eq!(message, "slow down");
            assert_eq!(*retry_after, Some(Duration::from_secs(7)));
        }
        other => panic!("unexpected {other:?}"),
    }
    assert!(error.is_retryable());
}

#[tokio::test]
async fn retry_after_may_be_an_http_date_and_nonsense_is_ignored() {
    let server = MockServer::start().await;
    let indexer = client(&server, ChainId::AlgorandMainnet);
    let mut results = Vec::new();
    for value in [httpdate::fmt_http_date(std::time::SystemTime::now() + Duration::from_secs(30)), "inf".to_owned(), "soon".to_owned()] {
        server.reset().await;
        Mock::given(path("/v1/algorand-mainnet/status"))
            .respond_with(ResponseTemplate::new(429).insert_header("Retry-After", value.as_str()).set_body_json(json!({ "error": "slow down" })))
            .mount(&server)
            .await;
        match indexer.status().await.unwrap_err() {
            Error::Api { retry_after, .. } => results.push(retry_after),
            other => panic!("unexpected {other:?}"),
        }
    }

    assert!(results[0].is_some_and(|d| d > Duration::ZERO && d <= Duration::from_secs(30)));
    assert_eq!(results[1], None);
    assert_eq!(results[2], None);
}

#[tokio::test]
async fn a_json_not_found_is_recognised() {
    let server = MockServer::start().await;
    Mock::given(path("/v1/algorand-mainnet/tokens/asa/1"))
        .respond_with(ResponseTemplate::new(404).set_body_json(json!({ "error": "Token not found." })))
        .mount(&server)
        .await;

    let error = client(&server, ChainId::AlgorandMainnet).token(TokenKind::Asa, 1).await.unwrap_err();

    assert!(error.is_not_found());
}

#[tokio::test]
async fn an_undeclared_status_keeps_its_body() {
    // For example the error page of a proxy in front of the indexer.
    let server = MockServer::start().await;
    Mock::given(path("/v1/algorand-mainnet/tokens/asa/1")).respond_with(ResponseTemplate::new(502).set_body_string("bad gateway")).mount(&server).await;

    let error = client(&server, ChainId::AlgorandMainnet).token(TokenKind::Asa, 1).await.unwrap_err();

    assert!(matches!(&error, Error::Api { status: 502, body: Some(body), .. } if body == "bad gateway"));
}

#[tokio::test]
async fn a_declared_error_status_without_json_is_a_decode_error() {
    let server = MockServer::start().await;
    Mock::given(path("/v1/algorand-mainnet/tokens/asa/1")).respond_with(ResponseTemplate::new(404).set_body_string("nope")).mount(&server).await;

    let error = client(&server, ChainId::AlgorandMainnet).token(TokenKind::Asa, 1).await.unwrap_err();

    assert!(matches!(error, Error::Decode(_)));
}

#[tokio::test]
async fn a_refused_connection_is_a_connection_error() {
    let indexer = AvmBalanceIndexer::builder(ChainId::AlgorandMainnet).base_url("http://127.0.0.1:1").timeout(Duration::from_secs(2)).build().unwrap();
    assert!(matches!(indexer.status().await, Err(Error::Connection(_))));
}

#[tokio::test]
async fn a_timeout_is_a_connection_error() {
    let server = MockServer::start().await;
    Mock::given(path("/v1/algorand-mainnet/status")).respond_with(ResponseTemplate::new(200).set_delay(Duration::from_millis(500))).mount(&server).await;
    let indexer = AvmBalanceIndexer::builder(ChainId::AlgorandMainnet).base_url(server.uri()).timeout(Duration::from_millis(100)).build().unwrap();

    assert!(matches!(indexer.status().await, Err(Error::Connection(_))));
}

#[tokio::test]
async fn holder_pages_are_streamed_with_the_round_pinned() {
    let server = MockServer::start().await;
    Mock::given(path("/v1/algorand-mainnet/tokens/asa/1/holders"))
        .and(query_param_is_missing("cursor"))
        .and(query_param_is_missing("round"))
        .respond_with(ResponseTemplate::new(200).set_body_json(holders_page("A", Some("c1"))))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(path("/v1/algorand-mainnet/tokens/asa/1/holders"))
        .and(query_param("cursor", "c1"))
        .and(query_param("round", "42"))
        .respond_with(ResponseTemplate::new(200).set_body_json(holders_page("B", Some("c2"))))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(path("/v1/algorand-mainnet/tokens/asa/1/holders"))
        .and(query_param("cursor", "c2"))
        .and(query_param("round", "42"))
        .respond_with(ResponseTemplate::new(200).set_body_json(holders_page("C", None)))
        .expect(1)
        .mount(&server)
        .await;
    let indexer = client(&server, ChainId::AlgorandMainnet);

    let holders: Vec<_> = indexer.holders_stream(TokenKind::Asa, 1, HoldersOptions::default()).try_collect().await.unwrap();

    assert_eq!(holders.iter().map(|h| h.account.as_str()).collect::<Vec<_>>(), ["A", "B", "C"]);
}

#[tokio::test]
async fn a_scan_is_started_with_its_request() {
    let server = MockServer::start().await;
    Mock::given(method("POST"))
        .and(path("/v1/algorand-mainnet/tokens/asa/1/holders/scans"))
        .and(wiremock::matchers::body_json(json!({ "round": 5 })))
        .respond_with(ResponseTemplate::new(202).set_body_json(scan("Pending", false, None)))
        .expect(1)
        .mount(&server)
        .await;

    let started = client(&server, ChainId::AlgorandMainnet)
        .start_holder_scan(TokenKind::Asa, 1, ScanRequest { round: Some(5), include_zero: None })
        .await
        .unwrap();

    assert_eq!(started.state, "Pending");
}

#[tokio::test]
async fn an_export_waits_for_the_scan_and_keeps_its_cursor() {
    let server = MockServer::start().await;
    let result_path = format!("/v1/algorand-mainnet/scans/{SCAN_ID}/holders");
    Mock::given(method("POST")).respond_with(ResponseTemplate::new(202).set_body_json(scan("Pending", false, None))).mount(&server).await;
    // Without a cursor: the first page, which points to c1.
    Mock::given(path(result_path.as_str()))
        .and(query_param_is_missing("cursor"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({ "id": SCAN_ID, "state": "Running", "complete": false, "round": 5, "items": [holder("A")], "nextCursor": "c1" }),
        ))
        .expect(1)
        .mount(&server)
        .await;
    // From c1: caught up with the running scan once (an empty page that points to c1 again), then the rest. Restarting without the cursor would repeat A.
    Mock::given(path(result_path.as_str()))
        .and(query_param("cursor", "c1"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({ "id": SCAN_ID, "state": "Running", "complete": false, "round": 5, "items": [], "nextCursor": "c1" }),
        ))
        .up_to_n_times(1)
        .mount(&server)
        .await;
    Mock::given(path(result_path.as_str()))
        .and(query_param("cursor", "c1"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({ "id": SCAN_ID, "state": "Completed", "complete": true, "round": 5, "items": [holder("B")], "nextCursor": null }),
        ))
        .mount(&server)
        .await;
    Mock::given(path(format!("/v1/algorand-mainnet/scans/{SCAN_ID}").as_str()))
        .respond_with(ResponseTemplate::new(200).set_body_json(scan("Running", false, None)))
        .mount(&server)
        .await;

    let holders: Vec<_> = client(&server, ChainId::AlgorandMainnet)
        .export_holders_stream(TokenKind::Asa, 1, ExportOptions { poll_interval: Some(Duration::from_millis(10)), ..Default::default() })
        .try_collect()
        .await
        .unwrap();

    assert_eq!(holders.iter().map(|h| h.account.as_str()).collect::<Vec<_>>(), ["A", "B"]);
}

#[tokio::test]
async fn dropping_an_export_early_cancels_the_scan() {
    let server = MockServer::start().await;
    Mock::given(method("POST")).respond_with(ResponseTemplate::new(202).set_body_json(scan("Pending", false, None))).mount(&server).await;
    Mock::given(method("DELETE")).respond_with(ResponseTemplate::new(200).set_body_json(scan("Cancelled", false, None))).expect(1).mount(&server).await;
    Mock::given(path(format!("/v1/algorand-mainnet/scans/{SCAN_ID}/holders").as_str()))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({ "id": SCAN_ID, "state": "Running", "complete": false, "round": 5, "items": [holder("A"), holder("B")], "nextCursor": "c1" }),
        ))
        .mount(&server)
        .await;
    let indexer = client(&server, ChainId::AlgorandMainnet);

    {
        let mut holders = std::pin::pin!(indexer.export_holders_stream(TokenKind::Asa, 1, ExportOptions::default()));
        assert_eq!(holders.try_next().await.unwrap().unwrap().account, "A");
    } // dropped here, before the scan ended

    for _ in 0..50 {
        if server.received_requests().await.unwrap().iter().any(|r| r.method.as_str() == "DELETE") {
            return;
        }
        tokio::time::sleep(Duration::from_millis(20)).await;
    }
    panic!("the scan was not cancelled");
}

#[tokio::test]
async fn the_items_of_a_page_arrive_before_a_failed_scan_is_reported() {
    let server = MockServer::start().await;
    Mock::given(method("POST")).respond_with(ResponseTemplate::new(202).set_body_json(scan("Running", false, None))).mount(&server).await;
    Mock::given(path(format!("/v1/algorand-mainnet/scans/{SCAN_ID}/holders").as_str()))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({ "id": SCAN_ID, "state": "Failed", "complete": false, "round": 5, "items": [holder("A")], "nextCursor": null }),
        ))
        .mount(&server)
        .await;
    Mock::given(path(format!("/v1/algorand-mainnet/scans/{SCAN_ID}").as_str()))
        .respond_with(ResponseTemplate::new(200).set_body_json(scan("Failed", false, Some("boom"))))
        .mount(&server)
        .await;
    let indexer = client(&server, ChainId::AlgorandMainnet);

    let mut holders = std::pin::pin!(indexer.export_holders_stream(TokenKind::Asa, 1, ExportOptions { poll_interval: Some(Duration::from_millis(10)), ..Default::default() }));

    assert_eq!(holders.try_next().await.unwrap().unwrap().account, "A");
    assert!(matches!(holders.try_next().await, Err(Error::Scan(_))));
}

#[tokio::test]
async fn an_export_validates_the_page_size_before_it_starts_a_scan() {
    let server = MockServer::start().await;
    let indexer = client(&server, ChainId::AlgorandMainnet);

    let mut holders = std::pin::pin!(indexer.export_holders_stream(TokenKind::Asa, 1, ExportOptions { limit: Some(0), ..Default::default() }));

    assert!(matches!(holders.try_next().await, Err(Error::InvalidArgument(_))));
    assert!(server.received_requests().await.unwrap().is_empty());
}

#[tokio::test]
async fn a_scan_that_fails_after_several_pages_is_reported_not_read_again_and_again() {
    let server = MockServer::start().await;
    let result_path = format!("/v1/algorand-mainnet/scans/{SCAN_ID}/holders");
    Mock::given(method("POST")).respond_with(ResponseTemplate::new(202).set_body_json(scan("Running", false, None))).mount(&server).await;
    Mock::given(path(result_path.as_str()))
        .and(query_param_is_missing("cursor"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({ "id": SCAN_ID, "state": "Running", "complete": false, "round": 5, "items": [holder("A")], "nextCursor": "c1" }),
        ))
        .mount(&server)
        .await;
    // The last page of a failed scan: rows, but no cursor and not complete.
    Mock::given(path(result_path.as_str()))
        .and(query_param("cursor", "c1"))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({ "id": SCAN_ID, "state": "Failed", "complete": false, "round": 5, "items": [holder("B")], "nextCursor": null }),
        ))
        .mount(&server)
        .await;
    Mock::given(path(format!("/v1/algorand-mainnet/scans/{SCAN_ID}").as_str()))
        .respond_with(ResponseTemplate::new(200).set_body_json(scan("Failed", false, Some("boom"))))
        .mount(&server)
        .await;
    let indexer = client(&server, ChainId::AlgorandMainnet);

    let mut holders = std::pin::pin!(indexer.export_holders_stream(TokenKind::Asa, 1, ExportOptions { poll_interval: Some(Duration::from_millis(10)), ..Default::default() }));

    assert_eq!(holders.try_next().await.unwrap().unwrap().account, "A");
    assert_eq!(holders.try_next().await.unwrap().unwrap().account, "B");
    assert!(matches!(holders.try_next().await, Err(Error::Scan(_))));
    let reads = server.received_requests().await.unwrap().iter().filter(|r| r.url.path().ends_with("/holders")).count();
    assert!(reads <= 3, "the pages were read {reads} times");
}

#[tokio::test]
async fn an_export_of_a_failed_scan_is_an_error() {
    let server = MockServer::start().await;
    Mock::given(method("POST")).respond_with(ResponseTemplate::new(202).set_body_json(scan("Failed", false, Some("boom")))).mount(&server).await;
    Mock::given(path(format!("/v1/algorand-mainnet/scans/{SCAN_ID}/holders").as_str()))
        .respond_with(ResponseTemplate::new(200).set_body_json(
            json!({ "id": SCAN_ID, "state": "Failed", "complete": false, "round": 5, "items": [], "nextCursor": null }),
        ))
        .mount(&server)
        .await;
    Mock::given(path(format!("/v1/algorand-mainnet/scans/{SCAN_ID}").as_str()))
        .respond_with(ResponseTemplate::new(200).set_body_json(scan("Failed", false, Some("boom"))))
        .mount(&server)
        .await;

    let result: Result<Vec<_>, _> = client(&server, ChainId::AlgorandMainnet)
        .export_holders_stream(TokenKind::Asa, 1, ExportOptions { poll_interval: Some(Duration::from_millis(10)), ..Default::default() })
        .try_collect()
        .await;

    let error = result.unwrap_err();
    assert!(matches!(&error, Error::Scan(scan) if scan.state == "Failed"));
    assert!(error.to_string().contains("boom"));
}

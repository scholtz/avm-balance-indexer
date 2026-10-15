# ScansApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**cancelHolderScan**](ScansApi.md#cancelholderscan) | **DELETE** /v1/{chain}/scans/{id} | Cancel a scan |
| [**createHolderScan**](ScansApi.md#createholderscan) | **POST** /v1/{chain}/tokens/{kind}/{tokenId}/holders/scans | Start an asynchronous export of every holder of a token at a round |
| [**getHolderScan**](ScansApi.md#getholderscan) | **GET** /v1/{chain}/scans/{id} | State and progress of a scan |
| [**getHolderScanResult**](ScansApi.md#getholderscanresult) | **GET** /v1/{chain}/scans/{id}/holders | Result pages of a scan (also while it is running) |



## cancelHolderScan

> ScanResponse cancelHolderScan(chain, id)

Cancel a scan

### Example

```ts
import {
  Configuration,
  ScansApi,
} from '';
import type { CancelHolderScanRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ScansApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // string
    id: id_example,
  } satisfies CancelHolderScanRequest;

  try {
    const data = await api.cancelHolderScan(body);
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters


| Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **chain** | `ChainId` | The chain this deployment serves. | [Defaults to `undefined`] [Enum: algorand-mainnet, algorand-testnet, voi] |
| **id** | `string` |  | [Defaults to `undefined`] |

### Return type

[**ScanResponse**](ScanResponse.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **400** | Bad Request |  -  |
| **404** | Not Found |  -  |
| **409** | Conflict |  -  |
| **429** | Too Many Requests |  -  |
| **503** | Service Unavailable |  -  |
| **504** | Gateway Timeout |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## createHolderScan

> ScanResponse createHolderScan(chain, kind, tokenId, scanRequest)

Start an asynchronous export of every holder of a token at a round

Accepted at once; poll the scan and read the result pages. The body is optional: { \&quot;round\&quot;: 123, \&quot;includeZero\&quot;: false }.

### Example

```ts
import {
  Configuration,
  ScansApi,
} from '';
import type { CreateHolderScanRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ScansApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // TokenKind | Token kind: algo (native), asa (Algorand Standard Asset, also ARC-20) or arc200 (application token).
    kind: ...,
    // number | Asset id, application id, or 0 for the native token.
    tokenId: 56,
    // ScanRequest (optional)
    scanRequest: ...,
  } satisfies CreateHolderScanRequest;

  try {
    const data = await api.createHolderScan(body);
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters


| Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **chain** | `ChainId` | The chain this deployment serves. | [Defaults to `undefined`] [Enum: algorand-mainnet, algorand-testnet, voi] |
| **kind** | `TokenKind` | Token kind: algo (native), asa (Algorand Standard Asset, also ARC-20) or arc200 (application token). | [Defaults to `undefined`] [Enum: algo, asa, arc200] |
| **tokenId** | `number` | Asset id, application id, or 0 for the native token. | [Defaults to `undefined`] |
| **scanRequest** | [ScanRequest](ScanRequest.md) |  | [Optional] |

### Return type

[**ScanResponse**](ScanResponse.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: `application/json`
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **202** | Accepted |  -  |
| **400** | Bad Request |  -  |
| **404** | Not Found |  -  |
| **429** | Too Many Requests |  -  |
| **503** | Service Unavailable |  -  |
| **504** | Gateway Timeout |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getHolderScan

> ScanResponse getHolderScan(chain, id)

State and progress of a scan

### Example

```ts
import {
  Configuration,
  ScansApi,
} from '';
import type { GetHolderScanRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ScansApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // string
    id: id_example,
  } satisfies GetHolderScanRequest;

  try {
    const data = await api.getHolderScan(body);
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters


| Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **chain** | `ChainId` | The chain this deployment serves. | [Defaults to `undefined`] [Enum: algorand-mainnet, algorand-testnet, voi] |
| **id** | `string` |  | [Defaults to `undefined`] |

### Return type

[**ScanResponse**](ScanResponse.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **400** | Bad Request |  -  |
| **404** | Not Found |  -  |
| **429** | Too Many Requests |  -  |
| **503** | Service Unavailable |  -  |
| **504** | Gateway Timeout |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


## getHolderScanResult

> ScanHoldersResponse getHolderScanResult(chain, id, cursor, limit)

Result pages of a scan (also while it is running)

Without a nextCursor and with complete&#x3D;false, repeat the same request a moment later.

### Example

```ts
import {
  Configuration,
  ScansApi,
} from '';
import type { GetHolderScanResultRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ScansApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // string
    id: id_example,
    // string | Opaque cursor from the previous page\'s nextCursor. (optional)
    cursor: cursor_example,
    // number | Page size, 1 to 1000 (default 100). (optional)
    limit: 56,
  } satisfies GetHolderScanResultRequest;

  try {
    const data = await api.getHolderScanResult(body);
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters


| Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **chain** | `ChainId` | The chain this deployment serves. | [Defaults to `undefined`] [Enum: algorand-mainnet, algorand-testnet, voi] |
| **id** | `string` |  | [Defaults to `undefined`] |
| **cursor** | `string` | Opaque cursor from the previous page\&#39;s nextCursor. | [Optional] [Defaults to `undefined`] |
| **limit** | `number` | Page size, 1 to 1000 (default 100). | [Optional] [Defaults to `undefined`] |

### Return type

[**ScanHoldersResponse**](ScanHoldersResponse.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **400** | Bad Request |  -  |
| **404** | Not Found |  -  |
| **429** | Too Many Requests |  -  |
| **503** | Service Unavailable |  -  |
| **504** | Gateway Timeout |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


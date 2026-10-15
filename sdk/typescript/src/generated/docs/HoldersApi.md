# HoldersApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**getTokenHolders**](HoldersApi.md#gettokenholders) | **GET** /v1/{chain}/tokens/{kind}/{tokenId}/holders | Holders of a token at the end of a round |



## getTokenHolders

> HoldersResponse getTokenHolders(chain, kind, tokenId, round, includeZero, cursor, limit)

Holders of a token at the end of a round

Holders in account order. For a very large token at a past round use an asynchronous scan. Pass the same round while paging.

### Example

```ts
import {
  Configuration,
  HoldersApi,
} from '';
import type { GetTokenHoldersRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new HoldersApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // TokenKind | Token kind: algo (native), asa (Algorand Standard Asset, also ARC-20) or arc200 (application token).
    kind: ...,
    // number | Asset id, application id, or 0 for the native token.
    tokenId: 56,
    // number | Chain round (end-of-round state). Omit for the latest published round. (optional)
    round: 56,
    // boolean | Also list accounts that are opted in but hold zero. (optional)
    includeZero: true,
    // string | Opaque cursor from the previous page\'s nextCursor. (optional)
    cursor: cursor_example,
    // number | Page size, 1 to 1000 (default 100). (optional)
    limit: 56,
  } satisfies GetTokenHoldersRequest;

  try {
    const data = await api.getTokenHolders(body);
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
| **round** | `number` | Chain round (end-of-round state). Omit for the latest published round. | [Optional] [Defaults to `undefined`] |
| **includeZero** | `boolean` | Also list accounts that are opted in but hold zero. | [Optional] [Defaults to `undefined`] |
| **cursor** | `string` | Opaque cursor from the previous page\&#39;s nextCursor. | [Optional] [Defaults to `undefined`] |
| **limit** | `number` | Page size, 1 to 1000 (default 100). | [Optional] [Defaults to `undefined`] |

### Return type

[**HoldersResponse**](HoldersResponse.md)

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


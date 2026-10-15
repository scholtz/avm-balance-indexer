# HistoryApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**getTokenHistory**](HistoryApi.md#gettokenhistory) | **GET** /v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/transactions | Balance effects of an account and token, newest first |



## getTokenHistory

> HistoryResponse getTokenHistory(chain, address, kind, tokenId, fromRound, toRound, cursor, limit)

Balance effects of an account and token, newest first

Every change of the balance with its signed delta, the balance after it and the transaction it came from. Limit the range with fromRound and toRound.

### Example

```ts
import {
  Configuration,
  HistoryApi,
} from '';
import type { GetTokenHistoryRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new HistoryApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // string
    address: address_example,
    // TokenKind | Token kind: algo (native), asa (Algorand Standard Asset, also ARC-20) or arc200 (application token).
    kind: ...,
    // number | Asset id, application id, or 0 for the native token.
    tokenId: 56,
    // number | Oldest round to include. Omit for no lower bound. (optional)
    fromRound: 56,
    // number | Newest round to include. Omit for no upper bound. (optional)
    toRound: 56,
    // string | Opaque cursor from the previous page\'s nextCursor. (optional)
    cursor: cursor_example,
    // number | Page size, 1 to 1000 (default 100). (optional)
    limit: 56,
  } satisfies GetTokenHistoryRequest;

  try {
    const data = await api.getTokenHistory(body);
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
| **address** | `string` |  | [Defaults to `undefined`] |
| **kind** | `TokenKind` | Token kind: algo (native), asa (Algorand Standard Asset, also ARC-20) or arc200 (application token). | [Defaults to `undefined`] [Enum: algo, asa, arc200] |
| **tokenId** | `number` | Asset id, application id, or 0 for the native token. | [Defaults to `undefined`] |
| **fromRound** | `number` | Oldest round to include. Omit for no lower bound. | [Optional] [Defaults to `undefined`] |
| **toRound** | `number` | Newest round to include. Omit for no upper bound. | [Optional] [Defaults to `undefined`] |
| **cursor** | `string` | Opaque cursor from the previous page\&#39;s nextCursor. | [Optional] [Defaults to `undefined`] |
| **limit** | `number` | Page size, 1 to 1000 (default 100). | [Optional] [Defaults to `undefined`] |

### Return type

[**HistoryResponse**](HistoryResponse.md)

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
| **429** | Too Many Requests |  -  |
| **503** | Service Unavailable |  -  |
| **504** | Gateway Timeout |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


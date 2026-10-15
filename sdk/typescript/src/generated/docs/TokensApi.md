# TokensApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**getToken**](TokensApi.md#gettoken) | **GET** /v1/{chain}/tokens/{kind}/{tokenId} | Token metadata |



## getToken

> TokenResponse getToken(chain, kind, tokenId)

Token metadata

### Example

```ts
import {
  Configuration,
  TokensApi,
} from '';
import type { GetTokenRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new TokensApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // TokenKind | Token kind: algo (native), asa (Algorand Standard Asset, also ARC-20) or arc200 (application token).
    kind: ...,
    // number | Asset id, application id, or 0 for the native token.
    tokenId: 56,
  } satisfies GetTokenRequest;

  try {
    const data = await api.getToken(body);
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

### Return type

[**TokenResponse**](TokenResponse.md)

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


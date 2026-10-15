# BalancesApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**getAccountTokens**](BalancesApi.md#getaccounttokens) | **GET** /v1/{chain}/accounts/{address}/tokens | Current token holdings of an account |
| [**getTokenBalance**](BalancesApi.md#gettokenbalance) | **GET** /v1/{chain}/accounts/{address}/tokens/{kind}/{tokenId}/balance | End-of-round balance of an account |



## getAccountTokens

> HoldingsResponse getAccountTokens(chain, address, includeZero, cursor, limit)

Current token holdings of an account

Holdings ordered by token kind and id. Zero balances (opt-ins) are listed with includeZero&#x3D;true. Page with nextCursor.

### Example

```ts
import {
  Configuration,
  BalancesApi,
} from '';
import type { GetAccountTokensRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new BalancesApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // string
    address: address_example,
    // boolean | Also list accounts that are opted in but hold zero. (optional)
    includeZero: true,
    // string | Opaque cursor from the previous page\'s nextCursor. (optional)
    cursor: cursor_example,
    // number | Page size, 1 to 1000 (default 100). (optional)
    limit: 56,
  } satisfies GetAccountTokensRequest;

  try {
    const data = await api.getAccountTokens(body);
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
| **includeZero** | `boolean` | Also list accounts that are opted in but hold zero. | [Optional] [Defaults to `undefined`] |
| **cursor** | `string` | Opaque cursor from the previous page\&#39;s nextCursor. | [Optional] [Defaults to `undefined`] |
| **limit** | `number` | Page size, 1 to 1000 (default 100). | [Optional] [Defaults to `undefined`] |

### Return type

[**HoldingsResponse**](HoldingsResponse.md)

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


## getTokenBalance

> BalanceResponse getTokenBalance(chain, address, kind, tokenId, round)

End-of-round balance of an account

The balance after the last effect at or before the round (default: the latest published round), including rounds without activity. An explicit published round is cacheable.

### Example

```ts
import {
  Configuration,
  BalancesApi,
} from '';
import type { GetTokenBalanceRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new BalancesApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
    // string
    address: address_example,
    // TokenKind | Token kind: algo (native), asa (Algorand Standard Asset, also ARC-20) or arc200 (application token).
    kind: ...,
    // number | Asset id, application id, or 0 for the native token.
    tokenId: 56,
    // number | Chain round (end-of-round state). Omit for the latest published round. (optional)
    round: 56,
  } satisfies GetTokenBalanceRequest;

  try {
    const data = await api.getTokenBalance(body);
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
| **round** | `number` | Chain round (end-of-round state). Omit for the latest published round. | [Optional] [Defaults to `undefined`] |

### Return type

[**BalanceResponse**](BalanceResponse.md)

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


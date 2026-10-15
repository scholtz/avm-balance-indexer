# StatusApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**getChainStatus**](StatusApi.md#getchainstatus) | **GET** /v1/{chain}/status | Indexed round, target round, lag and ingestion state |



## getChainStatus

> StatusResponse getChainStatus(chain)

Indexed round, target round, lag and ingestion state

### Example

```ts
import {
  Configuration,
  StatusApi,
} from '';
import type { GetChainStatusRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new StatusApi();

  const body = {
    // ChainId | The chain this deployment serves.
    chain: algorand-testnet,
  } satisfies GetChainStatusRequest;

  try {
    const data = await api.getChainStatus(body);
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

### Return type

[**StatusResponse**](StatusResponse.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `application/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | OK |  -  |
| **404** | Not Found |  -  |
| **429** | Too Many Requests |  -  |
| **503** | Service Unavailable |  -  |
| **504** | Gateway Timeout |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)


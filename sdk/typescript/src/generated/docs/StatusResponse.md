
# StatusResponse

Indexed round, target round, lag and ingestion state of the chain this deployment serves.

## Properties

Name | Type
------------ | -------------
`chain` | string
`provider` | string
`state` | string
`indexedRound` | number
`targetRound` | number
`lag` | number
`updatedAt` | Date

## Example

```typescript
import type { StatusResponse } from ''

// TODO: Update the object below with actual values
const example = {
  "chain": null,
  "provider": null,
  "state": null,
  "indexedRound": null,
  "targetRound": null,
  "lag": null,
  "updatedAt": null,
} satisfies StatusResponse

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as StatusResponse
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)




# HistoryResponse


## Properties

Name | Type
------------ | -------------
`account` | string
`token` | [TokenRef](TokenRef.md)
`indexedRound` | number
`items` | [Array&lt;EffectDto&gt;](EffectDto.md)
`nextCursor` | string

## Example

```typescript
import type { HistoryResponse } from ''

// TODO: Update the object below with actual values
const example = {
  "account": null,
  "token": null,
  "indexedRound": null,
  "items": null,
  "nextCursor": null,
} satisfies HistoryResponse

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as HistoryResponse
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)



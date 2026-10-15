
# HoldersResponse


## Properties

Name | Type
------------ | -------------
`token` | [TokenRef](TokenRef.md)
`round` | number
`indexedRound` | number
`items` | [Array&lt;HolderDto&gt;](HolderDto.md)
`nextCursor` | string

## Example

```typescript
import type { HoldersResponse } from ''

// TODO: Update the object below with actual values
const example = {
  "token": null,
  "round": null,
  "indexedRound": null,
  "items": null,
  "nextCursor": null,
} satisfies HoldersResponse

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as HoldersResponse
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)



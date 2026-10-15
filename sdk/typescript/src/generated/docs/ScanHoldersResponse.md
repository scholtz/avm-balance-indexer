
# ScanHoldersResponse


## Properties

Name | Type
------------ | -------------
`id` | string
`state` | string
`complete` | boolean
`round` | number
`items` | [Array&lt;HolderDto&gt;](HolderDto.md)
`nextCursor` | string

## Example

```typescript
import type { ScanHoldersResponse } from ''

// TODO: Update the object below with actual values
const example = {
  "id": null,
  "state": null,
  "complete": null,
  "round": null,
  "items": null,
  "nextCursor": null,
} satisfies ScanHoldersResponse

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as ScanHoldersResponse
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)



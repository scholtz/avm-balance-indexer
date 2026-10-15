
# HolderDto


## Properties

Name | Type
------------ | -------------
`account` | string
`amount` | string
`optedIn` | boolean
`lastChangedRound` | number

## Example

```typescript
import type { HolderDto } from ''

// TODO: Update the object below with actual values
const example = {
  "account": null,
  "amount": null,
  "optedIn": null,
  "lastChangedRound": null,
} satisfies HolderDto

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as HolderDto
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)



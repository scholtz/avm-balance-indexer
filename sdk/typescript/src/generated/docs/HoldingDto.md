
# HoldingDto


## Properties

Name | Type
------------ | -------------
`token` | [TokenRef](TokenRef.md)
`amount` | string
`optedIn` | boolean
`lastChangedRound` | number

## Example

```typescript
import type { HoldingDto } from ''

// TODO: Update the object below with actual values
const example = {
  "token": null,
  "amount": null,
  "optedIn": null,
  "lastChangedRound": null,
} satisfies HoldingDto

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as HoldingDto
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)




# EffectDto


## Properties

Name | Type
------------ | -------------
`round` | number
`intraRoundOrder` | number
`ordinal` | number
`transactionId` | string
`innerPath` | Array&lt;number&gt;
`type` | string
`delta` | string
`balanceAfter` | string
`counterparty` | string

## Example

```typescript
import type { EffectDto } from ''

// TODO: Update the object below with actual values
const example = {
  "round": null,
  "intraRoundOrder": null,
  "ordinal": null,
  "transactionId": null,
  "innerPath": null,
  "type": null,
  "delta": null,
  "balanceAfter": null,
  "counterparty": null,
} satisfies EffectDto

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as EffectDto
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)



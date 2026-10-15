
# TokenResponse


## Properties

Name | Type
------------ | -------------
`token` | [TokenRef](TokenRef.md)
`symbol` | string
`name` | string
`decimals` | number
`standard` | string
`standardVerified` | boolean
`creator` | string
`controllingAppId` | number
`createdRound` | number
`deletedRound` | number

## Example

```typescript
import type { TokenResponse } from ''

// TODO: Update the object below with actual values
const example = {
  "token": null,
  "symbol": null,
  "name": null,
  "decimals": null,
  "standard": null,
  "standardVerified": null,
  "creator": null,
  "controllingAppId": null,
  "createdRound": null,
  "deletedRound": null,
} satisfies TokenResponse

console.log(example)

// Convert the instance to a JSON string
const exampleJSON: string = JSON.stringify(example)
console.log(exampleJSON)

// Parse the JSON string back to an object
const exampleParsed = JSON.parse(exampleJSON) as TokenResponse
console.log(exampleParsed)
```

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)



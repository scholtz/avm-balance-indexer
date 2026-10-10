# Deployment

One Helm release per chain deployment, each with its own namespace, database, credentials and network policy:

| Release | Values overlay | Chain | Default provider |
|---|---|---|---|
| stage | `values/stage-testnet.yaml` | algorand-testnet | PostgreSQL |
| prod-algorand | `values/prod-algorand.yaml` | algorand-mainnet | PostgreSQL |
| prod-voi | `values/prod-voi.yaml` | voi | Cassandra |

Providers are chosen per release through `storage.provider` only; switching requires a redeploy and a replay into the
new backend. Databases themselves are provisioned by Kubernetes operators (CloudNativePG / K8ssandra or equivalent) and
are out of scope for this chart; the chart consumes existing Secrets.

```sh
helm upgrade --install avm-testnet deploy/helm/avm-balance-indexer \
  -n avm-testnet --create-namespace \
  -f deploy/helm/values/stage-testnet.yaml
```

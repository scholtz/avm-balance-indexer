# Deployment

One Helm release per chain deployment, each with its own namespace, database, credentials and network policy:

| Release | Values overlay | Chain | Default provider |
|---|---|---|---|
| stage | `values/stage-testnet.yaml` | algorand-testnet | PostgreSQL |
| prod-algorand | `values/prod-algorand.yaml` | algorand-mainnet | PostgreSQL |
| prod-voi | `values/prod-voi.yaml` | voi | PostgreSQL (Cassandra once implemented) |

Providers are chosen per release through `storage.provider` only; switching requires a redeploy and a replay into the
new backend. Databases themselves are provisioned by Kubernetes operators (CloudNativePG / K8ssandra or equivalent) and
are out of scope for this chart; the chart consumes existing Secrets.

```sh
helm upgrade --install avm-testnet deploy/helm/avm-balance-indexer \
  -n avm-testnet --create-namespace \
  -f deploy/helm/values/stage-testnet.yaml
```

## Chain data source

Each release runs one API deployment and one indexer worker. The worker fetches blocks from archival gossip nodes
(`algorand.gossipHosts`, or DNS SRV discovery when empty) and asks an algod node for the latest round
(`algorand.algodUrl`). For production, point both at your own archival nodes instead of the public endpoints.
Voi has no pinned genesis hash: set `algorand.expectedGenesisHash` after verifying it, or the worker refuses to start.
Keep `worker.replicaCount` at 1; the worker is the single ordered writer and has no leader election yet.

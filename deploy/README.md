# Deployment

The runbook for operators (sizing, upgrades, backup, monitoring, failure modes) is [docs/operations.md](../docs/operations.md).

One Helm release per chain deployment. The three standard releases live side by side in one namespace, `avm-balance-indexer`:

| Release | Values overlay | Chain | Host | Cassandra data (hostPath on the node) |
|---|---|---|---|---|
| `testnet` | `values/stage-testnet.yaml` | algorand-testnet | testnet.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/testnet` |
| `mainnet` | `values/prod-algorand.yaml` | algorand-mainnet | mainnet.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/mainnet` |
| `voi` | `values/prod-voi.yaml` | voi | voi.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/voi` |

Providers are chosen per release through `storage.provider` only; switching requires a redeploy and a replay into the new backend.
The overlays use Cassandra. Where no database operator (CloudNativePG / K8ssandra or equivalent) exists, the chart can start a
single-node Cassandra itself (`cassandra.embedded`, StatefulSet with a `hostPath` volume, pinned to a node with
`cassandra.embedded.nodeSelector`; a root init container fixes the directory permissions; a NetworkPolicy lets only the release's own pods reach port 9042). It has no login, so it
needs no Secret. With an external database or PostgreSQL the chart consumes existing Secrets instead.

```sh
kubectl apply -f deploy/rbac/deployer.yaml   # namespace and deployer account, once
helm upgrade --install testnet deploy/helm/avm-balance-indexer -n avm-balance-indexer -f deploy/helm/values/stage-testnet.yaml --set image.repository=docker.io/<dockerhub-user>/avm-balance-indexer --set image.tag=<tag>
```

The images are `<image.repository>-api` and `<image.repository>-worker`. The GitHub workflows that build, publish and deploy them are described in
[docs/deployment-pipelines.md](../docs/deployment-pipelines.md), which also lists every GitHub and Kubernetes secret.

## Chain data source

Each release runs one API deployment and the indexer workers. The worker fetches blocks from archival gossip nodes
(`algorand.gossipHosts`, or DNS SRV discovery when empty) and asks an algod node for the latest round
(`algorand.algodUrl`). For production, point both at your own archival nodes instead of the public endpoints.
The genesis hash is pinned for Algorand MainNet, TestNet and Voi; the worker refuses to start if the algod genesis or any
block does not match it. Set `algorand.expectedGenesisHash` only for another network.
The worker is the single ordered writer, but several replicas may run (`worker.replicaCount`, default 2): they compete for a writer lease, one indexes and the others stand by and take over when it is lost. Probes use `worker.healthPort`.

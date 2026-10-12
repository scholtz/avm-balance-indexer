# ADR 0013: One namespace, embedded single-node Cassandra on a hostPath, deployed from GitHub

- Status: accepted
- Date: 2026-10-11
- Issue: #77

## Context

The target cluster is a plain Kubernetes cluster without a database operator and with one fast local disk (`/mnt/nvme1`). The deployment has to be
automatic for testnet (every green push to `main`) and controlled for production (MainNet and Voi). PRODUCT-DESCRIPTION.md asks for separate scope per
chain deployment, and docs/operations.md names `ReplicationFactor` 1 a development setting.

## Decision

- **One namespace (`avm-balance-indexer`), three Helm releases** (`testnet`, `mainnet`, `voi`). The namespace-scoped kube config in `KUBE_CONFIG` can touch only this
  namespace (`deploy/rbac/deployer.yaml`). Isolation between chains is by release: separate keyspace, separate Cassandra instance and data directory, separate ConfigMap,
  and a NetworkPolicy that lets only the release's own pods reach its Cassandra.
- **Embedded Cassandra** (`cassandra.embedded`): a one-replica StatefulSet per release with the data in a `hostPath` directory
  (`/mnt/nvme1/avm-balance-indexer/<release>`). No login (reachable only through the NetworkPolicy), `replicationFactor` 1. Because the data belongs to one machine, the chart
  refuses to render without `cassandra.embedded.node` (or a nodeSelector); the pipeline takes it from the Actions variable `CASSANDRA_NODE`. Availability and backup of the data are the
  operator's job (disk snapshots or `nodetool snapshot`); a database operator (K8ssandra) stays the recommended route once a cluster exists, and replaces this option without code changes.
- **Images on Docker Hub** as `<repository>-api` and `<repository>-worker` (Docker Hub allows `user/repository` only). Testnet runs the immutable tag `main-<sha7>`, production the release tag.
- **Pipelines follow the upgrade rules of operations.md**: the deploy job upgrades the worker first (strategy `Recreate`, so no old writer runs while a migration is applied), waits for it,
  then upgrades everything to the new tag. Testnet deploys only the head of `main` after green CI; production needs a `vX.Y.Z` tag on a commit of `main` with a green CI run, and the
  protected `production` environment.
- **Secrets come from GitHub.** The deploy job creates the optional image pull secret and algod token secret with server-side apply on every run and restarts the pods when they change (hash in the pod template).
  The kube config (and, for a least-privilege account, `deploy/rbac/deployer.yaml`) is the only manual setup.

## Consequences

- A node loss loses the data of all chains on it until the directory is restored; indexing can be replayed from the chain (MainNet takes long).
- Three Cassandra JVMs share one machine; sizes are values in the overlays.
- Switching a release to an external Cassandra or PostgreSQL is a values change (`cassandra.embedded.enabled=false`, secrets as in docs/deployment-pipelines.md).

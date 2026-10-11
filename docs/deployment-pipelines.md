# Deployment pipelines and secrets

How the code reaches the Kubernetes cluster, and every secret that has to exist for it. The chart itself is described in
[deploy/README.md](../deploy/README.md); running it afterwards in [operations.md](operations.md).

| Workflow | Starts when | Does |
|---|---|---|
| [`deploy-testnet.yml`](../.github/workflows/deploy-testnet.yml) | CI succeeded on a push to `main` (or by hand) | pushes the images `main-<sha7>` and `testnet`, upgrades release `testnet` |
| [`deploy-production.yml`](../.github/workflows/deploy-production.yml) | a tag `vX.Y.Z` on a commit of `main` (or by hand with an existing image tag) | pushes `vX.Y.Z` and `latest`, upgrades release `mainnet`, then `voi` |
| [`docker-publish.yml`](../.github/workflows/docker-publish.yml), [`helm-deploy.yml`](../.github/workflows/helm-deploy.yml) | called by the two above | build and push to Docker Hub; `helm upgrade --install --wait` in the namespace `avm-balance-indexer`, then a smoke test of `https://<host>/v1/<chain>/status` |

What ends up where (one namespace, `avm-balance-indexer`, three releases):

| Release | Chain | Address | Cassandra data on the node |
|---|---|---|---|
| `testnet` | algorand-testnet | https://testnet.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/testnet` |
| `mainnet` | algorand-mainnet | https://mainnet.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/mainnet` |
| `voi` | voi | https://voi.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/voi` |

The mainnet and voi host names follow the testnet pattern; change them in `deploy/helm/values/prod-*.yaml` if you want others.

Images: `docker.io/<DOCKERHUB_USERNAME>/avm-balance-indexer-api` and `.../avm-balance-indexer-worker`. Testnet deploys the immutable
tag `main-<sha7>`; `testnet` and `latest` only move along for people who pull by hand.

## 1. GitHub secrets

Create them under Settings, Secrets and variables, Actions. Repository secrets work for all workflows; to keep production apart, create the
two **environments** `testnet` and `production` (Settings, Environments) and put `KUBE_CONFIG` into each (use a different kube config if the clusters differ).
On the `production` environment enable *Required reviewers* so that a release tag waits for an approval before it touches the cluster.

| Secret | Used by | Content |
|---|---|---|
| `DOCKERHUB_USERNAME` | image publish, deploy | Docker Hub account or organisation that owns the repositories. |
| `DOCKERHUB_TOKEN` | image publish | Docker Hub access token with *Read & Write* (Account settings, Security, New access token). Not your password. |
| `KUBE_CONFIG` | deploy | Kube config file of the deployer account (section 2), either the YAML itself or its base64 encoding. |

One **variable** (not a secret; Settings, Secrets and variables, Actions, Variables): `CASSANDRA_NODE`, the name of the node (`kubectl get nodes`) that owns
`/mnt/nvme1/avm-balance-indexer`. The chart pins the Cassandra pods to it and refuses to render without it, because on any other node the hostPath would be created empty and a new database would be indexed.

Docker Hub creates the repositories `avm-balance-indexer-api` and `avm-balance-indexer-worker` on the first push (create them first if your
organisation forbids that). Make them public, or give the cluster a pull secret (section 3).

## 2. Kubernetes: namespace, deployer account and kube config

Prerequisites in the cluster: an ingress controller (the chart leaves `ingress.className` empty, so the default class is used; set it in the overlay otherwise),
[cert-manager](https://cert-manager.io) with a `ClusterIssuer` named `letsencrypt-prod` (or change/remove `ingress.tls.clusterIssuer` in the overlays and provide the
TLS secret `<release>-<chain>-tls` yourself), DNS records for the three host names pointing at the ingress, and the data directories' disk (`/mnt/nvme1`) on the node that
runs Cassandra.

1. As a cluster admin apply the namespace and a namespace-scoped account: `kubectl apply -f deploy/rbac/deployer.yaml`. It may manage the chart's objects in
   `avm-balance-indexer` and nothing else (no cluster-wide rights, so the workflow cannot create the namespace).
2. Build the kube config for that account (replace the server address and the cluster name):

   ```sh
   NS=avm-balance-indexer
   SERVER=https://<api-server>:6443
   TOKEN=$(kubectl -n $NS get secret github-deployer-token -o jsonpath='{.data.token}' | base64 -d)
   CA=$(kubectl -n $NS get secret github-deployer-token -o jsonpath='{.data.ca\.crt}')
   cat > deployer.kubeconfig <<EOF
   apiVersion: v1
   kind: Config
   clusters:
     - name: cluster
       cluster: { server: $SERVER, certificate-authority-data: $CA }
   users:
     - name: github-deployer
       user: { token: $TOKEN }
   contexts:
     - name: deployer
       context: { cluster: cluster, user: github-deployer, namespace: $NS }
   current-context: deployer
   EOF
   base64 -w0 deployer.kubeconfig   # paste the output into the GitHub secret KUBE_CONFIG, then delete the file
   ```

   If you use your own kube config with rights in the namespace, paste that instead; the workflow only needs permission for the objects listed in `deploy/rbac/deployer.yaml`.
3. Cassandra stores its data in a directory of **one** node: set the variable `CASSANDRA_NODE` (section 1). The directory is created on first start and handed to uid 999.

## 3. Kubernetes secrets

With the shipped overlays **no Kubernetes Secret has to be created by hand**: the embedded Cassandra has no login (a NetworkPolicy lets only the release's own pods reach port 9042),
the images are public and the public algod/gossip endpoints need no token. Helm stores its release state as Secrets in the namespace; the deployer account may do that.
Create secrets only when you switch one of these options on:

| Need | Secret (in namespace `avm-balance-indexer`) | Chart value | Create it with |
|---|---|---|---|
| Private Docker Hub repositories | `dockerhub-pull`, type `kubernetes.io/dockerconfigjson` | `imagePullSecrets: [{name: dockerhub-pull}]` | `kubectl -n avm-balance-indexer create secret docker-registry dockerhub-pull --docker-username=<user> --docker-password=<access token>` (a token with *Read-only* rights is enough) |
| External Cassandra with login (instead of the embedded one) | keys `username`, `password` | `storage.cassandra.existingSecret`, plus `storage.cassandra.contactPoints`, `cassandra.embedded.enabled=false` | `kubectl -n avm-balance-indexer create secret generic cassandra-credentials --from-literal=username=<u> --from-literal=password=<p>` |
| PostgreSQL instead of Cassandra | key `connectionString` | `storage.provider=PostgreSql`, `storage.postgreSql.existingSecret` | `kubectl -n avm-balance-indexer create secret generic postgres --from-literal=connectionString='Host=...;Username=...;Password=...;Database=...'` |
| Algod node that needs a token | key `token` | `algorand.existingSecret`, `algorand.algodUrl` | `kubectl -n avm-balance-indexer create secret generic algod --from-literal=token=<token>` |
| Own TLS certificate (no cert-manager) | `<release>-<chain>-tls`, type `kubernetes.io/tls` | `ingress.tls.enabled=true`, `ingress.tls.clusterIssuer=""` | `kubectl -n avm-balance-indexer create secret tls testnet-algorand-testnet-tls --cert=tls.crt --key=tls.key` |

Create the secret **before** the deployment that uses it, and set the chart value in the overlay file (a commit to `main`, or `--set` for a manual `helm upgrade`).
The chart refuses to render when a required secret name is missing (`required` checks), so a forgotten value fails the workflow before anything changes.

## 4. First deployment and day-to-day

1. Add the secrets and the variable (1), prepare the cluster (2), push to `main`. The testnet workflow builds the images, installs the release and waits until the API,
   worker and Cassandra are ready (the first Cassandra start takes a few minutes; the wait is 25 minutes). The smoke test only warns, because DNS and the certificate can lag.
2. Production: merge to `main`, then `git tag v0.1.0 && git push origin v0.1.0`. Approve the run in the `production` environment. MainNet starts the backfill from round 0; plan disk and time
   (benchmark figures in the README) and consider your own archival nodes (`algorand.gossipHosts`).
3. Roll back or redeploy: Actions, *Deploy production*, Run workflow, give an existing image tag (for example `v0.1.0`). No images are built.
4. Look at it: `kubectl -n avm-balance-indexer get pods -l app.kubernetes.io/instance=testnet` and `kubectl -n avm-balance-indexer logs deploy/testnet-algorand-testnet-worker`.

Upgrades follow the rules of [operations.md](operations.md) automatically: when the release already exists, the deploy job first upgrades the worker alone
(`worker.strategy=Recreate`, so no old writer runs while a migration is applied), waits until it is ready, and then moves the API and everything else to the new tag. Indexing pauses for the
seconds a worker restart takes. Deployments of one release queue (they never overlap), testnet only ever deploys the head of `main`, and a production tag is refused unless CI succeeded on its commit.
For a rollback give *Deploy production* the image tag **and** the `ref` (tag or commit) whose chart and values belong to it.

Known limits of this setup: one Cassandra node per chain on one disk (no replication; back up the directories), `hostPath` ties the data to a machine, and the three Cassandra instances plus
the workers compete for the memory of that machine (see `cassandra.embedded.resources`).

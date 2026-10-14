# Deployment pipelines and secrets

How the code reaches the Kubernetes cluster, and every secret that has to exist for it. The chart itself is described in
[deploy/README.md](../deploy/README.md); running it afterwards in [operations.md](operations.md).

| Workflow | Starts when | Does |
|---|---|---|
| [`deploy-testnet.yml`](../.github/workflows/deploy-testnet.yml) | CI succeeded on a push to `main` (or by hand) | pushes the images `<version>-abi-<date>-<sha7>` and `testnet`, upgrades release `testnet` |
| [`deploy-production.yml`](../.github/workflows/deploy-production.yml) | a tag `vX.Y.Z` on a commit of `main` (or by hand with an existing image tag) | pushes `<version>-abi-<date>-<sha7>`, `vX.Y.Z` and `latest`, upgrades release `mainnet`, then `voi` |
| [`docker-publish.yml`](../.github/workflows/docker-publish.yml), [`helm-deploy.yml`](../.github/workflows/helm-deploy.yml) | called by the two above | build and push to Docker Hub; `helm upgrade --install --wait` in the namespace `avm-balance-indexer`, then a smoke test of `https://<host>/v1/<chain>/status` |

What ends up where (one namespace, `avm-balance-indexer`, three releases):

| Release | Chain | Address | Cassandra data on the node |
|---|---|---|---|
| `testnet` | algorand-testnet | https://testnet.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/testnet` |
| `mainnet` | algorand-mainnet | https://mainnet.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/mainnet` |
| `voi` | voi | https://voi.balance-indexer.biatec.io | `/mnt/nvme1/avm-balance-indexer/voi` |

The mainnet and voi host names follow the testnet pattern; change them in `deploy/helm/values/prod-*.yaml` if you want others.

Images: `docker.io/<DOCKERHUB_USERNAME>/avm-balance-indexer-api` and `.../avm-balance-indexer-worker`. Both environments deploy the immutable
tag `<version>-abi-<commit date>-<short commit>`, for example `1.0-abi-2026-10-13-abcdef1` (`abi` = avm balance indexer). `<version>` is the major.minor in the file [`VERSION`](../VERSION): raise it by hand in a
commit when a release changes the API or the data, and use a matching release tag (`VERSION` 1.0 needs `v1.0.N`; the workflow refuses others). The date is the commit's, so a rebuild gives the same tag (ADR 0014). The same string is the image label `org.opencontainers.image.version`.
`testnet`, `latest` and the release tag `vX.Y.Z` only move along for people who pull by hand.

## 1. GitHub secrets

Create them under Settings, Secrets and variables, Actions. Repository secrets work for all workflows; to keep production apart, create the
**environments** `testnet`, `mainnet` and `voi` (Settings, Environments; the workflows use the one named like the release; create them before the first release, a missing environment is created by GitHub without any protection) and put `KUBE_CONFIG` into each (use a different kube config if the clusters differ).
On `mainnet` and `voi` enable *Required reviewers* so that a release tag waits for an approval before it touches the cluster.

| Secret | Used by | Content |
|---|---|---|
| `DOCKERHUB_USERNAME` | image publish, deploy | Docker Hub account or organisation that owns the repositories. |
| `DOCKERHUB_TOKEN` | image publish, deploy | Docker Hub access token with *Read & Write* (Account settings, Security, New access token). Not your password. Only the pipeline uses it; it never enters the cluster. |
| `DOCKERHUB_PULL_TOKEN` | deploy, optional | A second, **read-only** Docker Hub access token; set it only when the repositories are private. Stored in the cluster as the image pull secret. |
| `ALGOD_TOKEN_TESTNET`, `ALGOD_TOKEN_MAINNET`, `ALGOD_TOKEN_VOI` | deploy, optional | API token of your own algod node for that chain (section 3). |
| `KUBE_CONFIG` | deploy | Kube config file of the deployer account (section 2), either the YAML itself or its base64 encoding. |

One **variable** (a secret works too, the variable wins when both exist, but a secret is masked as *** in logs; Settings, Secrets and variables, Actions): `CASSANDRA_NODE`, the name of the node (`kubectl get nodes`) that owns
`/mnt/nvme1/avm-balance-indexer`. The chart pins the Cassandra pods to it and refuses to render without it, because on any other node the hostPath would be created empty and a new database would be indexed.

Docker Hub creates the repositories `avm-balance-indexer-api` and `avm-balance-indexer-worker` on the first push (create them first if your
organisation forbids that). Make them public, or give the cluster a pull secret (section 3).

## 2. Kubernetes: namespace, deployer account and kube config

Prerequisites in the cluster: an ingress controller (the chart leaves `ingress.className` empty, so the default class is used; set it in the overlay otherwise),
[cert-manager](https://cert-manager.io) with a `ClusterIssuer` named `letsencrypt-dns` (or change/remove `ingress.tls.clusterIssuer` in the overlays and provide the
TLS secret `<release>-<chain>-tls` yourself), DNS records for the three host names pointing at the ingress, and the data directories' disk (`/mnt/nvme1`) on the node that
runs Cassandra.

The kube config is the only thing prepared by hand. Use any kube config that may manage the objects of the chart in the namespace `avm-balance-indexer` (a cluster-admin one also lets the job create the namespace). To get a least-privilege one:

1. As a cluster admin apply the namespace and a namespace-scoped account: `kubectl apply -f deploy/rbac/deployer.yaml`. It may manage the chart's objects in
   `avm-balance-indexer` and nothing else (no cluster-wide rights; the file creates the namespace, because this account cannot).
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
3. Cassandra stores its data in a directory of **one** node: set `CASSANDRA_NODE` (section 1). The directory is created on first start and handed to uid 999.

## 3. Kubernetes secrets: created by the pipeline

Nothing in the cluster is edited by hand: the deploy job creates and updates every Kubernetes Secret from the GitHub secrets, on each run (server-side apply, so the values are not copied into an annotation). A changed GitHub secret reaches the cluster with the next deployment, and the pods restart because the chart carries a hash of the secrets.

| Kubernetes Secret (namespace `avm-balance-indexer`) | Created from | Used for |
|---|---|---|
| `<release>-dockerhub-pull`, only when the GitHub secret exists | `DOCKERHUB_USERNAME` + `DOCKERHUB_PULL_TOKEN` | pulling the images from private Docker Hub repositories (`imagePullSecrets`); public repositories need nothing |
| `<release>-algod` (key `token`), only when the GitHub secret exists | `ALGOD_TOKEN_TESTNET`, `ALGOD_TOKEN_MAINNET`, `ALGOD_TOKEN_VOI` | an algod node that needs an API token (`algorand.existingSecret`); set the node address in the Actions variable or secret `ALGOD_URL_TESTNET` / `_MAINNET` / `_VOI` (without them the chain's public endpoint is used and no token is needed) |
| Helm release state | Helm itself | `sh.helm.release.*` Secrets, written with the deployer's rights |

The embedded Cassandra has no login and needs no Secret; certificates come from cert-manager (the `ClusterIssuer` of `ingress.tls.clusterIssuer`). The namespace is created by the job when it is missing and the kube config may do so;
otherwise `deploy/rbac/deployer.yaml` (section 2) creates it.

Not wired through the pipeline (the chart supports them with `existingSecret` values and a manual `helm upgrade`): PostgreSQL instead of Cassandra (secret with key `connectionString`, value `storage.postgreSql.existingSecret`),
a Cassandra with login (keys `username` and `password`, `storage.cassandra.existingSecret`, plus `storage.cassandra.contactPoints` and `cassandra.embedded.enabled=false`), and your own TLS certificate
(`kubernetes.io/tls` Secret `<release>-<chain>-tls` with `ingress.tls.clusterIssuer=""`).

## 4. First deployment and day-to-day

1. Add the secrets and the variable (1), prepare the cluster (2), push to `main`. The testnet workflow builds the images, installs the release and waits until the API,
   worker and Cassandra are ready (the first Cassandra start takes a few minutes; the wait is 25 minutes). The smoke test only warns, because DNS and the certificate can lag.
2. Production: merge to `main`, then `git tag v1.0.0 && git push origin v1.0.0`. Approve the runs in the `mainnet` and `voi` environments. MainNet starts the backfill from round 0; plan disk and time
   (benchmark figures in the README) and consider your own archival nodes (`algorand.gossipHosts`).
3. Roll back or redeploy: Actions, *Deploy production*, Run workflow, give an existing image tag (for example `v1.0.0`). No images are built.
4. Look at it: `kubectl -n avm-balance-indexer get pods -l app.kubernetes.io/instance=testnet` and `kubectl -n avm-balance-indexer logs deploy/testnet-algorand-testnet-worker`.

Upgrades follow the rules of [operations.md](operations.md) automatically: when the release already exists, the deploy job first upgrades the worker alone
(`worker.strategy=Recreate`, so no old writer runs while a migration is applied), waits until it is ready, and then moves the API and everything else to the new tag. Indexing pauses for the
seconds a worker restart takes. Deployments of one release never overlap (a run waits for the running one; of several waiting runs only the newest is kept, so start a rollback when nothing else is pending), testnet only ever deploys the head of `main`, and a production tag is refused unless CI succeeded on its commit.
For a rollback give *Deploy production* the image tag; the chart and values come from the commit in it (or from `ref` for older tags such as `main-<sha7>`).

Before anything changes the job checks every right in `deploy/rbac/deployer.yaml` that the chart and `helm --atomic` use and lists the missing ones. Only the NetworkPolicy and the PodDisruptionBudget are optional: a new release is installed without them (with a warning) when the account may not manage them, but an existing NetworkPolicy is never dropped, the job fails instead. Missing `delete` rights only produce a warning.
A failed upgrade is rolled back by Helm (`--atomic`). Each production job (MainNet, then Voi) asks the environment's reviewers for its own approval. The pods need a namespace without Pod Security enforcement
stricter than `privileged` for the Cassandra pod (it uses a hostPath volume and a root init container). Pre-release tags (`v1.2.3-rc1`) do not deploy.

Known limits of this setup: one Cassandra node per chain on one disk (no replication; back up the directories), `hostPath` ties the data to a machine, and the three Cassandra instances plus
the workers compete for the memory of that machine (see `cassandra.embedded.resources`).

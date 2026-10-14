# Publishing the SDKs

How the client libraries under [`sdk/`](../sdk/README.md) reach npm, PyPI, NuGet, crates.io and the Go module proxy, and which GitHub secrets that needs.

## What happens automatically

The workflow [`Publish SDKs`](../.github/workflows/sdk-publish.yml) runs when a change under `sdk/` is merged to `main` (and on demand: Actions, *Publish SDKs*, Run workflow).

1. **Verify**: the whole [`SDKs`](../.github/workflows/sdk.yml) workflow (versions, regenerated code, tests and packaging of all five languages). Nothing is published if it fails.
2. **Per registry**, in parallel: if the secret of that registry is configured and **the version in `sdk/VERSION` is not in the registry yet**, the package is tested again and published.
   Otherwise the job ends with a notice ("secret not set" or "already published"); that is not an error.
3. **Go**: the tag `sdk/go/vX.Y.Z` is created (the Go module proxy serves tags, there is nothing to upload).
4. **Release**: a GitHub release `sdk-vX.Y.Z` with generated notes, once.

So *code change merged* publishes only when the version was raised. Raise it in the same pull request: `sdk/scripts/set-version.sh 1.1.0` (all manifests) and commit. MAJOR = breaking change of the SDK surface,
MINOR = new feature, PATCH = fix or regenerated code. A pull request that changes SDK code without raising the version gets a warning annotation. A published version is never overwritten.

| Registry | Package | Secret / variable | Skipped without it |
|---|---|---|---|
| npm | `@scholtz/avm-balance-indexer` | secret `NPM_TOKEN` | yes |
| PyPI | `avm-balance-indexer` | secret `PYPI_API_TOKEN` **or** variable `PYPI_TRUSTED_PUBLISHING=true` | yes |
| NuGet | `AvmBalanceIndexer.Client` | secret `NUGET_API_KEY` | yes |
| crates.io | `avm-balance-indexer` | secret `CARGO_REGISTRY_TOKEN` | yes |
| Go modules | `github.com/scholtz/avm-balance-indexer/sdk/go` | none (the built-in `GITHUB_TOKEN` pushes the tag) | no |

The package names are the defaults of this repository. If a name is taken or you publish under another account, change it in `sdk/typescript/package.json`, `sdk/python/pyproject.toml`,
`sdk/dotnet/src/AvmBalanceIndexer.Client/AvmBalanceIndexer.Client.csproj` and `sdk/rust/Cargo.toml` (and the existence checks in `sdk-publish.yml`, which use the same names).

## Where to put the secrets

*Settings, Secrets and variables, Actions*. The publish jobs run in the GitHub environment **`sdk-publish`**, so you can store each secret either as a repository secret or as a secret of that environment
(*Settings, Environments, sdk-publish*). The environment is also the place to require a reviewer before anything is published (*Required reviewers*) or to limit it to the `main` branch (*Deployment branches*).
Create it before the first run if you want those protections; otherwise GitHub creates an unprotected one on first use.

## npm

1. Create the scope: the package is `@scholtz/avm-balance-indexer`, so your npm user or organisation must be named `scholtz` (or change the name, see above).
2. npmjs.com, *Access Tokens*, *Generate New Token*, **Granular Access Token**: permissions *Read and write* for the package (for the first publish select the scope/organisation, then restrict to the package afterwards),
   no expiry longer than you are willing to rotate, and enable **Bypass two-factor authentication** (an automation token cannot answer a 2FA prompt).
3. GitHub secret **`NPM_TOKEN`** = the token.

Packages are published with `--provenance` (a signed statement linking the package to this repository and workflow run); that needs the repository to be public.

## PyPI

Two ways; the token is the simple one, Trusted Publishing has no long-lived secret.

*API token*

1. pypi.org, *Account settings*, *API tokens*, *Add API token*. For the first release the token must have scope **Entire account** (the project does not exist yet); after the first upload create a token scoped to the project `avm-balance-indexer` and replace it.
2. GitHub secret **`PYPI_API_TOKEN`** = the token (`pypi-…`).

*Trusted Publishing* (recommended once the project exists)

1. pypi.org, the project, *Publishing*, *Add a new publisher*: owner `scholtz`, repository `avm-balance-indexer`, workflow `sdk-publish.yml`, environment `sdk-publish`.
   (For the very first release use *pending publishers* under *Your account, Publishing*.)
2. GitHub variable **`PYPI_TRUSTED_PUBLISHING`** = `true` (a *variable*, not a secret) and **no** `PYPI_API_TOKEN`.

## NuGet

1. nuget.org, *API Keys*, *Create*: key name `avm-balance-indexer`, *Push new packages and package versions*, glob pattern `AvmBalanceIndexer.Client*`, an expiry you can rotate.
   The id prefix `AvmBalanceIndexer.` should be reserved for your account (*Account, Reserve ID prefix*) so nobody else can publish under it.
2. GitHub secret **`NUGET_API_KEY`** = the key.

The workflow pushes the `.nupkg` and the symbols package (`.snupkg`).

## crates.io

1. crates.io, *Account Settings*, *API Tokens*, *New Token*: name `github-avm-balance-indexer`, scopes **publish-new** and **publish-update**, optionally limited to the crate pattern `avm-balance-indexer`.
   Your GitHub account must have a verified email address on crates.io.
2. GitHub secret **`CARGO_REGISTRY_TOKEN`** = the token.

## Go

No secret. The module lives in the sub-directory `sdk/go` and is versioned by the tag `sdk/go/vX.Y.Z`, which the workflow pushes with the repository's `GITHUB_TOKEN` (the job has `contents: write`).
Requirements: the repository is public (otherwise consumers set `GOPRIVATE`), and tag creation is allowed for the workflow if you use *tag protection rules* / rulesets (allow `github-actions[bot]` to create `sdk/go/v*`).
Major versions 2 and higher need the module path to end in `/v2` (`go.mod`, imports, docs): plan that before raising `sdk/VERSION` to 2.0.0.

## Checking a release

After a run, *Actions, Publish SDKs* shows one job per registry; a job that did nothing says why in its summary notice. To verify by hand:

```sh
npm view @scholtz/avm-balance-indexer version
pip index versions avm-balance-indexer
dotnet package search AvmBalanceIndexer.Client --exact-match     # or nuget.org
cargo search avm-balance-indexer
go list -m github.com/scholtz/avm-balance-indexer/sdk/go@latest
```

## Rotating or revoking a token

Create the new token, replace the GitHub secret, revoke the old token. Published versions stay available; a bad release is *deprecated* (`npm deprecate`, PyPI *yank*, NuGet *deprecate*, `cargo yank`, Go `retract` in `go.mod`) and fixed with a higher version.

## Local dry runs

```sh
cd sdk/typescript && npm pack --dry-run
cd sdk/python && python -m build && python -m twine check dist/*
cd sdk/dotnet && dotnet pack src/AvmBalanceIndexer.Client -c Release
cd sdk/rust && cargo package --allow-dirty
```

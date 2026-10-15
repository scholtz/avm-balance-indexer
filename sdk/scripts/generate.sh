#!/usr/bin/env bash
# Regenerates the generated part of every SDK from sdk/openapi/avm-balance-indexer.v1.json.
#
#   sdk/scripts/generate.sh            all languages
#   sdk/scripts/generate.sh go python  only these
#
# Needs Docker (openapi-generator for TypeScript and Python, jq to strip `format: uuid` for Python, golang for oapi-codegen) and the .NET SDK (NSwag, pinned in sdk/dotnet/dotnet-tools.json).
# CI runs this and fails when it changes a committed file (workflow "SDKs").
set -euo pipefail

OPENAPI_GENERATOR_IMAGE="openapitools/openapi-generator-cli:v7.26.0"
GO_IMAGE="golang:1.25"
OAPI_CODEGEN="github.com/oapi-codegen/oapi-codegen/v2/cmd/oapi-codegen@v2.8.0"
PROGENITOR_VERSION="0.15.1"
RUST_NIGHTLY="nightly-2026-10-01"

sdk_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repo_dir="$(cd "$sdk_dir/.." && pwd)"
spec_rel="sdk/openapi/avm-balance-indexer.v1.json"

# Docker wants a native path on Windows (Git Bash) and the POSIX one elsewhere; keep file ownership on Linux.
if mount_dir="$(cd "$repo_dir" && pwd -W 2>/dev/null)"; then user_args=(); else mount_dir="$repo_dir"; user_args=(-u "$(id -u):$(id -g)"); fi
export MSYS_NO_PATHCONV=1

# Downloads of the Rust generator are cached here (git-ignored; the workflow caches this directory).
cache_dir="${AVM_SDK_CACHE:-$sdk_dir/.cache}"
mkdir -p "$cache_dir"
cache_mount="$(cd "$cache_dir" && { pwd -W 2>/dev/null || pwd; })"

docker_run() { docker run --rm "${user_args[@]}" -v "$mount_dir:/repo" "$@"; }

# Generated files are committed with LF endings whatever the OS that generated them.
normalize() { find "$1" -type f \( -name '*.ts' -o -name '*.py' -o -name '*.cs' -o -name '*.go' -o -name '*.rs' -o -name '*.json' -o -name '*.md' \) -exec perl -pi -e 's/\r$//' {} +; }

generate_typescript() {
  local out="sdk/typescript/src/generated"
  rm -rf "${repo_dir:?}/$out"
  docker_run -w /repo "$OPENAPI_GENERATOR_IMAGE" generate -i "$spec_rel" -g typescript-fetch -o "/repo/$out" \
    --additional-properties=importFileExtension=.js,supportsES6=true,typescriptThreePlus=true,withInterfaces=false \
    --global-property=apiTests=false,modelTests=false,apiDocs=false,modelDocs=false > /dev/null
  rm -rf "${repo_dir:?}/$out/.openapi-generator" "${repo_dir:?}/$out/.openapi-generator-ignore"
  normalize "$repo_dir/$out"
}

generate_python() {
  local out="sdk/python/src/avm_balance_indexer/_generated" tmp="sdk/.generated-python" spec_py="sdk/.spec-python.json"
  rm -rf "${repo_dir:?}/$out" "${repo_dir:?}/$tmp"
  trap 'rm -f "$repo_dir/$spec_py"' RETURN
  command -v jq > /dev/null || { echo "generate.sh: jq is required to generate the Python client (apt install jq, choco install jq)" >&2; exit 1; }
  # openapi-generator >= 7.2x types `format: uuid` as uuid.UUID and ignores --type-mappings for it (the type comes from the
  # format in PythonClientCodegen.getPydanticType; tried with 7.26.0). 1.0.0 exposed these ids as StrictStr and callers compare
  # and serialize them as strings, so the python generator reads a copy of the document without that format.
  (cd "$repo_dir" && jq 'walk(if type == "object" and .format == "uuid" then del(.format) else . end)' "$spec_rel" > "$spec_py")
  docker_run -w /repo "$OPENAPI_GENERATOR_IMAGE" generate -i "$spec_py" -g python -o "/repo/$tmp" \
    --additional-properties=packageName=avm_balance_indexer._generated,projectName=avm-balance-indexer,library=urllib3 \
    --global-property=apiTests=false,modelTests=false,apiDocs=false,modelDocs=false > /dev/null
  rm -f "$repo_dir/$spec_py"
  mv "$repo_dir/$tmp/avm_balance_indexer/_generated" "$repo_dir/$out"
  rm -rf "${repo_dir:?}/$tmp"
  normalize "$repo_dir/$out"
}

generate_dotnet() {
  local generator_log
  generator_log="$(mktemp)"
  local out="sdk/dotnet/src/AvmBalanceIndexer.Client/Generated/GeneratedClient.cs"
  (
    cd "$sdk_dir/dotnet"
    dotnet tool restore > /dev/null
    dotnet tool run nswag openapi2csclient \
      /input:../openapi/avm-balance-indexer.v1.json \
      /output:src/AvmBalanceIndexer.Client/Generated/GeneratedClient.cs \
      /namespace:AvmBalanceIndexer.Client.Generated /className:GeneratedClient \
      /operationGenerationMode:SingleClientFromOperationId /jsonLibrary:SystemTextJson \
      /injectHttpClient:true /disposeHttpClient:false /useBaseUrl:false /generateClientInterfaces:false \
      /generateExceptionClasses:true /exceptionClass:GeneratedApiException /dateTimeType:System.DateTimeOffset \
      /generateNullableReferenceTypes:true /generateOptionalParameters:true /generateDtoTypes:true /classStyle:Poco \
      /clientClassAccessModifier:internal /generateSyncMethods:false /requiredPropertiesMustBeDefined:true \
      /arrayType:System.Collections.Generic.IReadOnlyList /arrayInstanceType:System.Collections.Generic.List > "$generator_log" || { cat "$generator_log"; exit 1; }
  )
  normalize "$repo_dir/$(dirname "$out")"
}

generate_go() {
  docker_run -e HOME=/tmp -e GOCACHE=/tmp/go-build -e GOPATH=/tmp/go -w /repo/sdk/go "$GO_IMAGE" sh -c "go run $OAPI_CODEGEN -config oapi-codegen.yaml ../openapi/avm-balance-indexer.v1.json" > /dev/null
  normalize "$sdk_dir/go"
}

generate_rust() {
  # progenitor writes a whole crate; only its client code is kept (sdk/rust/src/generated.rs). Its formatter needs a nightly rustfmt, which is pinned
  # together with the generator version so that the output is reproducible. The cargo volumes only cache downloads between runs.
  mkdir -p "$cache_dir/cargo-registry" "$cache_dir/cargo-tools"
  docker run --rm -v "$mount_dir:/repo" -v "$cache_mount/cargo-registry:/usr/local/cargo/registry" -v "$cache_mount/cargo-tools:/opt/tools" -w /repo/sdk rust:1 sh -c "
    set -e
    rustup toolchain install $RUST_NIGHTLY --profile minimal -c rustfmt > /dev/null
    [ -x /opt/tools/bin/cargo-progenitor ] || cargo install cargo-progenitor --version $PROGENITOR_VERSION --locked --root /opt/tools --quiet
    rm -rf /tmp/generated-rust
    RUSTUP_TOOLCHAIN=$RUST_NIGHTLY /opt/tools/bin/cargo-progenitor progenitor -i openapi/avm-balance-indexer.v1.json -o /tmp/generated-rust \
      -n avm-balance-indexer-api -v 1.0.0 --interface builder --license-name MIT > /dev/null
    cp /tmp/generated-rust/src/lib.rs rust/src/generated.rs
    chown -R $(id -u):$(id -g) rust/src/generated.rs /opt/tools /usr/local/cargo/registry
  "
  normalize "$sdk_dir/rust/src"
}

targets=("$@")
if [ ${#targets[@]} -eq 0 ]; then targets=(typescript python dotnet go rust); fi
for target in "${targets[@]}"; do
  echo "==> $target"
  "generate_$target"
done

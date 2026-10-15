#!/usr/bin/env bash
# Fails unless every SDK manifest carries the version in sdk/VERSION (the Go module is versioned by git tag only).
set -euo pipefail
sdk_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
expected="$(tr -d '[:space:]' < "$sdk_dir/VERSION")"
if ! [[ "$expected" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then echo "sdk/VERSION must be MAJOR.MINOR.PATCH, found '$expected'"; exit 1; fi

status=0
check() { # name found
  if [ "$2" != "$expected" ]; then echo "MISMATCH $1: $2 (sdk/VERSION is $expected)"; status=1; else echo "ok       $1: $2"; fi
}

check typescript/package.json "$(sed -nE 's/^  "version": "([^"]+)".*/\1/p' "$sdk_dir/typescript/package.json" | head -1)"
check typescript/package-lock.json "$(sed -nE 's/^  "version": "([^"]+)".*/\1/p' "$sdk_dir/typescript/package-lock.json" | head -1)"
check python/pyproject.toml "$(sed -nE 's/^version = "([^"]+)".*/\1/p' "$sdk_dir/python/pyproject.toml" | head -1)"
check dotnet/AvmBalanceIndexer.Client.csproj "$(sed -nE 's#.*<Version>([^<]+)</Version>.*#\1#p' "$sdk_dir/dotnet/src/AvmBalanceIndexer.Client/AvmBalanceIndexer.Client.csproj" | head -1)"
check rust/Cargo.toml "$(sed -nE 's/^version = "([^"]+)".*/\1/p' "$sdk_dir/rust/Cargo.toml" | head -1)"
exit $status

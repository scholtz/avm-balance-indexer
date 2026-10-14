#!/usr/bin/env bash
# sdk/scripts/set-version.sh 1.2.3  - writes the version into sdk/VERSION and every SDK manifest (needs bash and python3; no network, any OS).
set -euo pipefail
version="${1:?usage: set-version.sh MAJOR.MINOR.PATCH}"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then echo "version must be MAJOR.MINOR.PATCH"; exit 1; fi
sdk_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

python3 - "$version" "$sdk_dir" <<'PY'
import json
import pathlib
import re
import sys

version, sdk = sys.argv[1], pathlib.Path(sys.argv[2])


def substitute(path, pattern):
    file = sdk / path
    text = file.read_text(encoding="utf-8")
    changed, count = re.subn(pattern, lambda m: m.group(1) + version + m.group(2), text, count=1, flags=re.M)
    if count == 0:
        sys.exit(f"{path}: no version found")
    file.write_text(changed, encoding="utf-8", newline="")


(sdk / "VERSION").write_text(version + "\n", encoding="utf-8", newline="")
substitute("typescript/package.json", r'^(  "version": ")[^"]+(")')
substitute("python/pyproject.toml", r'^(version = ")[^"]+(")')
substitute("dotnet/src/AvmBalanceIndexer.Client/AvmBalanceIndexer.Client.csproj", r"(<Version>)[^<]+(</Version>)")
substitute("rust/Cargo.toml", r'^(version = ")[^"]+(")')

# package-lock.json repeats the version of the root package.
lock_path = sdk / "typescript/package-lock.json"
lock = json.loads(lock_path.read_text(encoding="utf-8"))
lock["version"] = version
lock["packages"][""]["version"] = version
lock_path.write_text(json.dumps(lock, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="")
PY

bash "$sdk_dir/scripts/check-versions.sh"

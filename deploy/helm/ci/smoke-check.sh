#!/usr/bin/env bash
# Checks a smoke release (.github/workflows/k8s-smoke.yml) through its API: the configured provider serves, the worker indexes
# TestNet, and a historical balance is answered. Usage: smoke-check.sh <PostgreSql|Cassandra> [namespace] [release]
set -euo pipefail

provider="$1"
ns="${2:-smoke}"
release="${3:-smoke}"
base="http://localhost:8080"
api="$base/v1/algorand-testnet"
# TestNet fee sink: in the genesis allocation and paid every round's fees, so its balance at round 10 is above zero.
sink=A7NMWS3NT3IUDMLVO26ULGXGIIOUQ3ND2TXSER6EBGRZNOBOUIQXHIBGDE

kubectl -n "$ns" port-forward "svc/$release-algorand-testnet-api" 8080:80 > /tmp/port-forward.log 2>&1 &
pf=$!
trap 'kill "$pf" 2>/dev/null || true' EXIT
for _ in $(seq 1 30); do curl -fsS "$base/healthz/ready" > /dev/null 2>&1 && break; sleep 2; done
curl -fsS "$base/healthz/ready" > /dev/null

# --wait only waited for readiness (a standby worker is ready too): the published status is the real check.
status=""
for _ in $(seq 1 120); do
  status="$(curl -fsS "$api/status" || true)"
  if [ -n "$status" ] && [ "$(jq -r '.provider' <<< "$status")" = "$provider" ] && [ "$(jq -r '.indexedRound // 0' <<< "$status")" -ge 20 ]; then
    break
  fi
  sleep 5
done
echo "status: $status"
test "$(jq -r '.provider' <<< "$status")" = "$provider" || { echo "::error::the API reports provider $(jq -r '.provider' <<< "$status"), expected $provider"; exit 1; }
test "$(jq -r '.indexedRound // 0' <<< "$status")" -ge 20 || { echo "::error::the worker did not index 20 TestNet rounds in 10 minutes"; exit 1; }

balance="$(curl -fsS "$api/accounts/$sink/tokens/algo/0/balance?round=10")"
echo "balance: $balance"
amount="$(jq -r '.amount' <<< "$balance")"
[[ "$amount" =~ ^[1-9][0-9]*$ ]] || { echo "::error::the fee sink balance at round 10 is $amount, expected a positive amount"; exit 1; }

if [ "$provider" = Cassandra ]; then
  # The start command override of the embedded Cassandra (autoSnapshotTtl) reached cassandra.yaml.
  setting="$(kubectl -n "$ns" exec "deploy/$release-algorand-testnet-cassandra" -c cassandra -- \
    cqlsh -e "SELECT value FROM system_views.settings WHERE name='auto_snapshot_ttl'")"
  echo "$setting"
  grep -qw 1d <<< "$setting" || { echo "::error::auto_snapshot_ttl is not 1d"; exit 1; }
fi
echo "smoke test OK: $provider"

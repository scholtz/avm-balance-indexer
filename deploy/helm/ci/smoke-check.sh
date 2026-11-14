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
# A forward to a service sticks to one pod and ends when that pod restarts: stop at once with its log instead of polling a dead port.
forwarding() { kill -0 "$pf" 2>/dev/null || { echo "::error::kubectl port-forward ended"; cat /tmp/port-forward.log; exit 1; }; }
for _ in $(seq 1 30); do forwarding; curl -fsS "$base/healthz/ready" > /dev/null 2>&1 && break; sleep 2; done
curl -sS --fail-with-body "$base/healthz/ready" > /dev/null

# --wait only waited for readiness (a standby worker is ready too): the published status is the real check.
status=""
for _ in $(seq 1 120); do
  forwarding
  status="$(curl -fsS --max-time 5 "$api/status" || true)"
  if [ -n "$status" ] && [ "$(jq -r '.provider' <<< "$status")" = "$provider" ] && [ "$(jq -r '.indexedRound // 0' <<< "$status")" -ge 20 ]; then
    break
  fi
  sleep 5
done
echo "status: $status"
test "$(jq -r '.provider' <<< "$status")" = "$provider" || { echo "::error::the API reports provider $(jq -r '.provider' <<< "$status"), expected $provider"; exit 1; }
test "$(jq -r '.indexedRound // 0' <<< "$status")" -ge 20 || { echo "::error::the worker did not index 20 TestNet rounds in 10 minutes"; exit 1; }

balance="$(curl -sS --fail-with-body --max-time 30 "$api/accounts/$sink/tokens/algo/0/balance?round=10")" || { echo "::error::balance request failed: $balance"; exit 1; }
echo "balance: $balance"
amount="$(jq -r '.amount' <<< "$balance")"
[[ "$amount" =~ ^[1-9][0-9]*$ ]] || { echo "::error::the fee sink balance at round 10 is $amount, expected a positive amount"; exit 1; }

# /status names the configured provider; that the schema exists in that store shows the services really use it.
if [ "$provider" = Cassandra ]; then
  tables="$(kubectl -n "$ns" exec "deploy/$release-algorand-testnet-cassandra" -c cassandra -- \
    cqlsh -e "SELECT table_name FROM system_schema.tables WHERE keyspace_name='avm_indexer'")"
  grep -q "rows)" <<< "$tables" && ! grep -q "(0 rows)" <<< "$tables" || { echo "::error::no tables in keyspace avm_indexer: $tables"; exit 1; }
else
  tables="$(kubectl -n "$ns" exec deploy/postgres -- psql -U postgres -tAc \
    "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')")"
  [ "$tables" -gt 0 ] || { echo "::error::no indexer tables in PostgreSQL"; exit 1; }
fi
echo "schema in $provider: OK"

if [ "$provider" = Cassandra ]; then
  # The start command override of the embedded Cassandra (autoSnapshotTtl) reached cassandra.yaml.
  setting="$(kubectl -n "$ns" exec "deploy/$release-algorand-testnet-cassandra" -c cassandra -- \
    cqlsh -e "SELECT value FROM system_views.settings WHERE name='auto_snapshot_ttl'")"
  echo "$setting"
  grep -qw 1d <<< "$setting" || { echo "::error::auto_snapshot_ttl is not 1d"; exit 1; }
fi
echo "smoke test OK: $provider"

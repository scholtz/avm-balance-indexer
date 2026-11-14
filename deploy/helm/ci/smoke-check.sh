#!/usr/bin/env bash
# Checks a smoke release (.github/workflows/k8s-smoke.yml) through its API and its database: the configured provider serves and
# holds the schema, the worker indexes TestNet, and a historical balance is answered; on Cassandra also the start command override
# and the network policy. Usage: smoke-check.sh <PostgreSql|Cassandra> [namespace] [release]; SMOKE_PORT picks the local port.
set -euo pipefail

provider="$1"
ns="${2:-smoke}"
release="${3:-smoke}"
port="${SMOKE_PORT:-8080}"
base="http://localhost:$port"
api="$base/v1/algorand-testnet"
fail() { echo "::error::$*"; exit 1; }
# TestNet fee sink: in the genesis allocation and paid every round's fees, so its balance at round 10 is above zero.
sink=A7NMWS3NT3IUDMLVO26ULGXGIIOUQ3ND2TXSER6EBGRZNOBOUIQXHIBGDE

log="$(mktemp)"
kubectl -n "$ns" port-forward "svc/$release-algorand-testnet-api" "$port:80" > "$log" 2>&1 &
pf=$!
trap 'kill "$pf" 2>/dev/null || true; rm -f "$log" "$log.psql"' EXIT
# A forward to a service sticks to one pod and ends when that pod restarts: stop at once with its log instead of polling a dead port.
forwarding() { kill -0 "$pf" 2>/dev/null || { cat "$log"; fail "kubectl port-forward ended"; }; }
for _ in $(seq 1 30); do forwarding; curl -fsS "$base/healthz/ready" > /dev/null 2>&1 && break; sleep 2; done
ready="$(curl -sS --fail-with-body --max-time 10 "$base/healthz/ready")" || fail "the API is not ready: $ready"

# --wait only waited for readiness (a standby worker is ready too): the published status is the real check. At most 10 minutes.
deadline=$((SECONDS + 600))
status=""
while [ "$SECONDS" -lt "$deadline" ]; do
  forwarding
  status="$(curl -fsS --max-time 5 "$api/status" || true)"
  if jq -e '.provider' > /dev/null 2>&1 <<< "$status"; then
    # The provider is configuration: the first answer is final.
    [ "$(jq -r '.provider' <<< "$status")" = "$provider" ] || fail "the API reports provider $(jq -r '.provider' <<< "$status"), expected $provider"
    [ "$(jq -r '.indexedRound // 0' <<< "$status")" -ge 20 ] && break
  fi
  sleep 5
done
echo "status: $status"
[ -n "$status" ] && [ "$(jq -r '.indexedRound // 0' <<< "$status")" -ge 20 ] || fail "the worker did not index 20 TestNet rounds in 10 minutes"

balance="$(curl -sS --fail-with-body --max-time 30 "$api/accounts/$sink/tokens/algo/0/balance?round=10")" || fail "balance request failed: $balance"
echo "balance: $balance"
amount="$(jq -r '.amount' <<< "$balance")"
[[ "$amount" =~ ^[1-9][0-9]*$ ]] || fail "the fee sink balance at round 10 is $amount, expected a positive amount"

# /status names the configured provider; that the schema exists in that store shows the services really use it.
if [ "$provider" = Cassandra ]; then
  cassandra="deploy/$release-algorand-testnet-cassandra"
  tables="$(kubectl -n "$ns" exec "$cassandra" -c cassandra -- \
    cqlsh -e "SELECT table_name FROM system_schema.tables WHERE keyspace_name='avm_indexer'" 2>&1)" || fail "cqlsh failed: $tables"
  grep -q "rows)" <<< "$tables" && ! grep -q "(0 rows)" <<< "$tables" || fail "no tables in keyspace avm_indexer: $tables"

  # The start command override of the embedded Cassandra (autoSnapshotTtl) reached cassandra.yaml.
  setting="$(kubectl -n "$ns" exec "$cassandra" -c cassandra -- \
    cqlsh -e "SELECT value FROM system_views.settings WHERE name='auto_snapshot_ttl'" 2>&1)" || fail "cqlsh failed: $setting"
  echo "$setting"
  grep -qw 1d <<< "$setting" || fail "auto_snapshot_ttl is not 1d"

  # The network policy: a pod with this release's labels reaches CQL, any other pod does not.
  # probe <pod> <labels> <attempts>: a new pod may send its first packet before the policy knows its labels, so allowed probes retry.
  probe() {
    kubectl -n "$ns" run "$1" --image=busybox:1.37 --restart=Never --rm -i --quiet --labels="$2" --command -- \
      sh -c "for i in \$(seq 1 $3); do nc -z -w 5 $release-algorand-testnet-cassandra 9042 && { echo OPEN; exit; }; sleep 2; done; echo BLOCKED" 2>&1 || true
  }
  allowed="$(probe cql-allowed "app.kubernetes.io/instance=$release,avm.scholtz.sk/chain=algorand-testnet" 5)"
  grep -q OPEN <<< "$allowed" || fail "a pod of the release cannot reach Cassandra: $allowed"
  other="$(probe cql-other "app=other" 1)"
  grep -q BLOCKED <<< "$other" || fail "the network policy lets any pod reach Cassandra: $other"
  # Blocked only counts if Cassandra was reachable around it (not a restart or a DNS failure in between).
  again="$(probe cql-allowed-again "app.kubernetes.io/instance=$release,avm.scholtz.sk/chain=algorand-testnet" 5)"
  grep -q OPEN <<< "$again" || fail "Cassandra was unreachable after the blocked probe, so it proves nothing: $again"
else
  tables="$(kubectl -n "$ns" exec deploy/postgres -- psql -U postgres -tAc \
    "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')" 2>"$log.psql")" || fail "psql failed: $(cat "$log.psql")"
  [ "$tables" -gt 0 ] || fail "no indexer tables in PostgreSQL"
fi
echo "smoke test OK: $provider"

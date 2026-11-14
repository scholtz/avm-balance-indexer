#!/usr/bin/env bash
# Alert rules (templates/prometheusrule.yaml): render checks, promtool check, and the unit tests in alerts.test.yaml.
# Run from the repository root; needs helm, yq and docker. CI runs it in the Helm job.
set -euo pipefail

d="$(mktemp -d)"
trap 'rm -rf "$d"' EXIT
t() { helm template ci deploy/helm/avm-balance-indexer -f deploy/helm/values/stage-testnet.yaml --set cassandra.embedded.node=ci-node --set telemetry.otlpEndpoint=http://otel:4317 "$@"; }
refused() { if t "$@" > /dev/null 2>&1; then echo "must fail the render: $*"; exit 1; fi; }

if grep -q "kind: PrometheusRule" <<< "$(t)"; then echo "alert rules must be off by default"; exit 1; fi
refused --set alerts.enabled=true --set telemetry.otlpEndpoint=
refused --set alerts.enabled=true --set alerts.api5xxRatio=5
refused --set alerts.enabled=true --set alerts.apiP95Seconds=10
for bad in 0 -1 abc 0.4; do refused --set alerts.enabled=true --set "alerts.lagRounds=$bad"; done
refused --set alerts.enabled=true --set alerts.statusAgeSeconds=0.3

# Fractions given with --set arrive as strings and are valid; the annotations print numbers plainly.
out=$(t --set alerts.enabled=true --set alerts.api5xxRatio=0.07 --set alerts.apiP95Seconds=2.5)
grep -q "more than 7 percent" <<< "$out"
grep -q "> 0.07" <<< "$out"
grep -q "> 0.05" <<< "$(t --set alerts.enabled=true --set alerts.api5xxRatio=.05)"
# Large numbers from a values file (float64, printed as 1e+06) are valid thresholds.
printf 'alerts:\n  enabled: true\n  lagRounds: 1000000\n' > "$d/big.yaml"
grep -q "more than 1000000 rounds" <<< "$(t -f "$d/big.yaml")"

# promtool: the full rules are valid; the unit tests check expressions, durations and labels, not the annotation texts.
t --set alerts.enabled=true | yq 'select(.kind == "PrometheusRule") | .spec' > "$d/rules-full.yaml"
test "$(yq '[.groups[].rules[]] | length' "$d/rules-full.yaml")" = 9
yq 'del(.groups[].rules[].annotations)' "$d/rules-full.yaml" > "$d/rules.yaml"
docker run --rm -v "$d/rules-full.yaml:/rules-full.yaml:ro" --entrypoint promtool prom/prometheus:v3.5.0 check rules /rules-full.yaml
docker run --rm -v "$PWD/deploy/helm/ci/alerts.test.yaml:/rules/alerts.test.yaml:ro" -v "$d/rules.yaml:/rules/rules.yaml:ro" -w /rules \
  --entrypoint promtool prom/prometheus:v3.5.0 test rules alerts.test.yaml

# The no-writer delay follows the Cassandra lease: 30 s TTL -> 3 minutes, 300 s -> 6 minutes.
grep -q "for: 180s" <<< "$(t --set alerts.enabled=true)"
grep -q "for: 360s" <<< "$(t --set alerts.enabled=true --set storage.cassandra.leaseTtlSeconds=300)"
# Extra labels are merged with the chart labels, not duplicated.
test "$(t --set alerts.enabled=true --set 'alerts.labels.app\.kubernetes\.io/part-of=monitoring' | yq 'select(.kind == "PrometheusRule") | .metadata.labels."app.kubernetes.io/part-of"')" = monitoring
grep -q 'avm_chain="voi"' <<< "$(helm template ci deploy/helm/avm-balance-indexer -f deploy/helm/values/prod-voi.yaml --set cassandra.embedded.node=ci-node --set telemetry.otlpEndpoint=http://otel:4317 --set alerts.enabled=true)"
grep -q 'release="x"' <<< "$(t --set alerts.enabled=true --set 'alerts.selector=release="x"')"
echo "alert rules OK"

#!/usr/bin/env bash
# Restarts the api replicas one at a time, keeping every run (introduce-plugins decision 2), for an in-process plugin.
#   1. out of rotation: compose/lb/conf.d/http/00-api.conf becomes, transiently, the OTHER replicas' addresses, and the
#      balancer reloads, so new requests and new keepalive connections go only to them;
#   2. drain: `docker restart -t 40` — the api's ShutdownTimeout is 30 s, so Kestrel finishes its runs (or records the
#      rest cancelled) before Docker's grace ends;
#   3. healthy again; 4. back in rotation; 5. next replica.
# At the end, and on Ctrl+C or SIGTERM, the name-based form (compose/lb/api.upstream.conf) is restored and reloaded,
# so the balancer never keeps a stale address list. With one replica it refuses unless ALLOW_DOWNTIME=1.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export MAF_LAB_REPO="${MAF_LAB_REPO:-$ROOT}"
COMPOSE=(docker compose -p "${COMPOSE_PROJECT_NAME:-maf-lab}")
CONF="$ROOT/compose/lb/conf.d/http/00-api.conf"
GRACE=40

reload_lb() { "${COMPOSE[@]}" exec -T lb sh -c 'nginx -t -c /etc/nginx/lb/nginx.conf -q && nginx -c /etc/nginx/lb/nginx.conf -s reload'; }
restore() {
  python3 "$ROOT/scripts/plugins.py" install --conf-d-only >/dev/null 2>&1 || cp "$ROOT/compose/lb/api.upstream.conf" "$CONF"
  reload_lb >/dev/null 2>&1 || true
}
STOP=0
trap 'STOP=1; echo; echo "… stopping after the current replica" >&2' INT TERM
trap restore EXIT

replicas=()
while read -r id; do [[ -n "$id" ]] && replicas+=("$id"); done < <("${COMPOSE[@]}" ps -q api)
count=${#replicas[@]}
if (( count == 0 )); then echo "✗ no api replica is running" >&2; exit 1; fi
if (( count == 1 )) && [[ "${ALLOW_DOWNTIME:-0}" != 1 ]]; then
  echo "✗ only one api replica: restarting it interrupts the api. Run with ALLOW_DOWNTIME=1, or scale up first (API_REPLICAS=2)" >&2
  exit 2
fi

address() { docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}' "$1" | awk '{print $1}'; }
health() { docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$1"; }
write_without() {
  local skip="$1" tmp="$CONF.tmp-$$"
  {
    echo "# Transient (scripts/api_restart.sh): the replicas other than the one restarting. make restores the template."
    echo "upstream api_pool {"
    echo "    zone api_pool 64k;"
    echo "    least_conn;"
    for id in "${replicas[@]}"; do [[ "$id" == "$skip" ]] || echo "    server $(address "$id"):8080 max_fails=1 fail_timeout=10s;"; done
    echo "    keepalive 32;"
    echo "}"
  } >"$tmp"
  mv "$tmp" "$CONF"
}

i=0
for id in "${replicas[@]}"; do
  i=$((i + 1))
  name="$(docker inspect -f '{{.Name}}' "$id" | sed 's#^/##')"
  if (( count > 1 )); then
    echo "[$i/$count] $name: out of rotation"
    write_without "$id"
    reload_lb
  fi
  echo "[$i/$count] $name: draining and restarting (up to ${GRACE}s)"
  # In the background and waited for, so a Ctrl+C is noted at once but lands after this replica (see plugin_switch.sh).
  ( trap '' INT TERM; docker restart -t "$GRACE" "$id" >/dev/null ) &
  pid=$!
  while :; do
    wait "$pid" && rc=0 || rc=$?
    kill -0 "$pid" 2>/dev/null || break
  done
  (( rc == 0 )) || { echo "✗ $name did not restart" >&2; exit 1; }
  start=$SECONDS
  until [[ "$(health "$id")" == healthy ]]; do
    if (( SECONDS - start > 180 )); then echo "✗ $name did not become healthy" >&2; exit 1; fi
    sleep 2
  done
  echo "[$i/$count] $name: healthy, back in rotation"
  # The restarted replica keeps its container but may change address: rebuild the list from scratch next round.
  restore
  if [[ "$STOP" == 1 ]]; then echo "Stopped after $name; the name-based upstream is restored" >&2; exit 130; fi
done
echo "✓ api replicas restarted ($count)"

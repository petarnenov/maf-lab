#!/usr/bin/env bash
# Waits until every running compose service with a healthcheck is healthy and every one-shot service exited 0.
# On timeout (or a failed one-shot) prints the offending services with their last 30 log lines and exits 1.
# Usage: scripts/wait_healthy.sh [timeout_seconds]   (compose file: $COMPOSE_FILE or compose/docker-compose.yml)
set -euo pipefail
TIMEOUT="${1:-300}"
FILE="${COMPOSE_FILE:-$(cd "$(dirname "$0")/.." && pwd)/compose/docker-compose.yml}"
compose() { docker compose -f "$FILE" "$@"; }

status() {
  # Prints "<container> <service> <state> <health> <exitcode>" per container.
  compose ps -a --format json | python3 -c '
import json, sys
for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    items = json.loads(line)
    for c in (items if isinstance(items, list) else [items]):
        print(c["Name"], c["Service"], c["State"], c.get("Health") or "-", c.get("ExitCode", 0))
'
}

deadline=$((SECONDS + TIMEOUT))
while :; do
  pending=()
  failed=()
  while read -r name service state health code; do
    if [[ "$state" == "exited" ]]; then
      [[ "$code" == "0" ]] || failed+=("$name (exited $code)")
    elif [[ "$state" != "running" ]]; then
      pending+=("$name ($state)")
    elif [[ "$health" != "-" && "$health" != "healthy" ]]; then
      pending+=("$name ($health)")
    fi
  done < <(status)

  if (( ${#failed[@]} > 0 )) || { (( ${#pending[@]} > 0 )) && (( SECONDS >= deadline )); }; then
    echo "✗ services not ready:" >&2
    for s in ${failed[@]+"${failed[@]}"} ${pending[@]+"${pending[@]}"}; do
      echo "  - $s" >&2
      echo "    ── last 30 log lines ──" >&2
      docker logs --tail 30 "${s%% *}" 2>&1 | sed 's/^/    /' >&2
    done
    exit 1
  fi
  if (( ${#pending[@]} == 0 )); then
    echo "✓ all services healthy"
    exit 0
  fi
  sleep 2
done

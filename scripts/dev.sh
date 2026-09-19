#!/usr/bin/env bash
# Local development without the balancer: Qdrant + Ollama in compose, mcp-retrieval (:5090), api (:5080) and the Vite
# dev server (:5174) as foreground processes with prefixed output. Ctrl-C stops all three.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FILE="$ROOT/compose/docker-compose.yml"
DOTNET="${DOTNET:-dotnet}"
NPM="${NPM:-npm}"
export Models__OllamaEndpoint="${Models__OllamaEndpoint:-http://localhost:11435}"

docker compose -f "$FILE" up -d qdrant ollama ollama-init
docker compose -f "$FILE" stop lb api mcp-retrieval web >/dev/null 2>&1 || true

pids=()
kill_tree() { # dotnet run and npm start child processes: stop the whole tree
  local pid="$1" child
  for child in $(pgrep -P "$pid" 2>/dev/null); do kill_tree "$child"; done
  kill -TERM "$pid" 2>/dev/null || true
}
cleanup() {
  trap - INT TERM EXIT
  echo "stopping dev processes…"
  for pid in ${pids[@]+"${pids[@]}"}; do kill_tree "$pid"; done
  wait 2>/dev/null || true
}
trap cleanup INT TERM EXIT

run() { # name dir command...
  local name="$1" dir="$2"; shift 2
  # Process substitution keeps $! pointing at the server process itself, not at the output prefixer.
  ( cd "$dir" && exec "$@" ) > >(sed -u "s/^/[$name] /") 2>&1 &
  pids+=($!)
}

"$DOTNET" build "$ROOT/maf-lab.sln" -v q -nologo
run mcp "$ROOT/src/Maf.Lab.Retrieval" "$DOTNET" run --no-build
run api "$ROOT/src/Maf.Lab.Api" "$DOTNET" run --no-build
run web "$ROOT/web" "$NPM" run dev
echo "dev: web http://localhost:5174 · api http://localhost:5080 · mcp http://localhost:5090/mcp (Ctrl-C to stop)"
wait

#!/usr/bin/env bash
# Local development without the balancer: Qdrant, Neo4j + both Ollama instances in compose, mcp-retrieval (:5090), mcp-portfolio (:5091),
# mcp-code (:5092), api (:5080) and the Vite dev server (:5174) as foreground processes with prefixed output. Ctrl-C stops all.
set -euo pipefail
# compose mounts the repository at MAF_LAB_REPO (make exports it); outside make, it is this checkout.
export MAF_LAB_REPO="${MAF_LAB_REPO:-$(git -C "$(dirname "$0")/.." rev-parse --show-toplevel)}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FILE="$ROOT/compose/docker-compose.yml"
DOTNET="${DOTNET:-dotnet}"
NPM="${NPM:-npm}"
export Models__OllamaEndpoint="${Models__OllamaEndpoint:-http://localhost:11435}"
export Models__OllamaNumThread="${Models__OllamaNumThread:-${OLLAMA_INTERACTIVE_THREADS:-4}}"
export Models__BatchOllamaEndpoint="${Models__BatchOllamaEndpoint:-http://localhost:11436}"
export Models__BatchOllamaNumThread="${Models__BatchOllamaNumThread:-${OLLAMA_BATCH_THREADS:-12}}"
# The graph store's Bolt port, published on loopback, with the compose password.
export Neo4j__Uri="${Neo4j__Uri:-bolt://localhost:7687}"
export Neo4j__Password="${Neo4j__Password:-${NEO4J_PASSWORD:-maf-lab-dev-graph}}"

docker compose -f "$FILE" up -d qdrant neo4j ollama ollama-batch ollama-init ollama-warm
# Every plugin's services stop too, first among them those that share lb's network (the inspectors).
plugin_files=(); plugin_services=()
for dir in "$ROOT"/plugins/*/; do
  [ -f "$dir/compose.yml" ] || continue
  plugin_files+=(-f "$dir/compose.yml")
  while read -r service; do plugin_services+=("$service"); done < <(python3 "$ROOT/scripts/plugins.py" services "$(basename "$dir")")
done
docker compose -f "$FILE" ${plugin_files[@]+"${plugin_files[@]}"} stop ${plugin_services[@]+"${plugin_services[@]}"} \
  lb api mcp-retrieval mcp-portfolio mcp-code web >/dev/null 2>&1 || true

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
# The api reads the installed plugins from the repository's plugins/ (compose mounts it at /plugins); the set is the one
# make resolves (MAF_PLUGINS, MAF_ENV), written here as `make up` writes it.
export Plugins__Root="$ROOT/plugins"
python3 "$ROOT/scripts/plugins.py" install --installed-only
installed() { python3 "$ROOT/scripts/plugins.py" resolve | grep -qx "$1"; }
run mcp "$ROOT/src/Maf.Lab.Retrieval" "$DOTNET" run --no-build
run portfolio "$ROOT/src/Maf.Lab.Portfolio" "$DOTNET" run --no-build
# The codebase's server runs only while the code plugin is installed. Its server.json names the balancer, which make dev
# bypasses, so the api is pointed at the local one by the configured override of that plugin's server.
if installed code; then
  export Agent__Servers__code__Endpoint=http://localhost:5092/mcp
  run code "$ROOT/src/Maf.Lab.CodeSearch" "$DOTNET" run --no-build
fi
run api "$ROOT/src/Maf.Lab.Api" "$DOTNET" run --no-build
run web "$ROOT/web" "$NPM" run dev
echo "dev: web http://localhost:5174 · api http://localhost:5080 · mcp http://localhost:5090/mcp · portfolio http://localhost:5091/mcp · code http://localhost:5092/mcp (Ctrl-C to stop)"
wait

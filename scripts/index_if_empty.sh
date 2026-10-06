#!/usr/bin/env bash
# Indexes each domain's corpus only when its Qdrant collection is missing or empty, then builds its graph when the graph
# store holds no node of that source.
#   index_if_empty.sh                  the built-in domains: portfolio (data-portfolio/)
#   index_if_empty.sh --source NAME    one plugin's corpus, as its plugin.mk describes it in the environment (Qdrant__Collection,
#                                      Qdrant__MetaCollection, Indexing__CorpusRoot, Indexing__*), and its graph source NAME
# Env: QDRANT_URL (http://localhost:6333), DOTNET (dotnet),
#      Models__OllamaEndpoint (http://localhost:11435 — the compose Ollama for queries), Models__BatchOllamaEndpoint
#      (http://localhost:11436 — the one for documents), their thread counts, NEO4J_PASSWORD (the compose default).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
QDRANT_URL="${QDRANT_URL:-http://localhost:6333}"
DOTNET="${DOTNET:-dotnet}"
export Models__OllamaEndpoint="${Models__OllamaEndpoint:-http://localhost:11435}"
export Models__OllamaNumThread="${Models__OllamaNumThread:-${OLLAMA_INTERACTIVE_THREADS:-4}}"
export Models__BatchOllamaEndpoint="${Models__BatchOllamaEndpoint:-http://localhost:11436}"
export Models__BatchOllamaNumThread="${Models__BatchOllamaNumThread:-${OLLAMA_BATCH_THREADS:-12}}"

# One corpus and one collection per domain: portfolio (data-portfolio/ → maf_portfolio_chunks), and each plugin's (its
# plugin.mk calls this with --source).
index_domain() {
  local collection="$1" corpus="$2" meta="$3"
  local points
  points=$(curl -sf "$QDRANT_URL/collections/$collection" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"].get("points_count") or 0)' 2>/dev/null || echo 0)
  if [[ "$points" -gt 0 ]]; then
    echo "✓ index present ($collection: $points chunks) — skipping indexing"
    return 0
  fi
  echo "… index $collection is empty — indexing $corpus (embeddings via $Models__BatchOllamaEndpoint)"
  Indexing__CorpusRoot="$corpus" Qdrant__Collection="$collection" Qdrant__MetaCollection="$meta" \
    "$DOTNET" run --project "$ROOT/src/Maf.Lab.Indexing" -- index
}

# The graph: a source's nodes are counted through cypher-shell in the neo4j container, so the host needs no Neo4j client.
graph_if_empty() {
  local source="$1" nodes
  nodes=$(MAF_LAB_REPO="${MAF_LAB_REPO:-$ROOT}" docker compose -f "$ROOT/compose/docker-compose.yml" exec -T neo4j \
    cypher-shell -u neo4j -p "${NEO4J_PASSWORD:-maf-lab-dev-graph}" --format plain \
    "MATCH (n) WHERE n.source = '$source' RETURN count(n) AS nodes" 2>/dev/null | tail -1 | tr -dc '0-9' || true)
  if [[ "${nodes:-0}" -gt 0 ]]; then
    echo "✓ graph present ($source: $nodes nodes) — skipping its build"
  else
    echo "… graph has no $source nodes — building them"
    Neo4j__Uri="${Neo4j__Uri:-bolt://localhost:7687}" Neo4j__Password="${NEO4J_PASSWORD:-maf-lab-dev-graph}" \
      "$DOTNET" run --project "$ROOT/src/Maf.Lab.Indexing" -- graph --only "$source"
  fi
}

if [[ "${1:-}" == "--source" ]]; then
  # One plugin's part: its plugin.mk runs this only while the plugin's folder exists.
  [[ -n "${2:-}" ]] || { echo "usage: index_if_empty.sh [--source NAME]" >&2; exit 2; }
  index_domain "${Qdrant__Collection:?its plugin.mk names the collection}" "${Indexing__CorpusRoot:?its plugin.mk names the corpus}" "${Qdrant__MetaCollection:?its plugin.mk names the meta collection}"
  graph_if_empty "$2"
  exit 0
fi

# A plugin's corpus is indexed only while it is installed (introduce-plugins task 2.4); a part still in the core (no
# plugins/<name>/ folder yet) always is.
installed() { [[ ! -f "$ROOT/plugins/$1/plugin.toml" ]] || python3 "$ROOT/scripts/plugins.py" resolve | grep -qx "$1"; }
if installed portfolio; then
  index_domain maf_portfolio_chunks "$ROOT/data-portfolio" maf_portfolio_meta
fi

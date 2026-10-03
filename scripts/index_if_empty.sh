#!/usr/bin/env bash
# Indexes each domain's corpus only when its Qdrant collection is missing or empty.
# Then builds the graph when the graph store holds no nodes.
# Env: QDRANT_URL (http://localhost:6333), Qdrant__Collection (maf_chunks), DOTNET (dotnet),
#      Models__OllamaEndpoint (http://localhost:11435 — the compose Ollama for queries), Models__BatchOllamaEndpoint
#      (http://localhost:11436 — the one for documents), their thread counts, NEO4J_PASSWORD (the compose default).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
QDRANT_URL="${QDRANT_URL:-http://localhost:6333}"
COLLECTION="${Qdrant__Collection:-maf_chunks}"
DOTNET="${DOTNET:-dotnet}"
export Models__OllamaEndpoint="${Models__OllamaEndpoint:-http://localhost:11435}"
export Models__OllamaNumThread="${Models__OllamaNumThread:-${OLLAMA_INTERACTIVE_THREADS:-4}}"
export Models__BatchOllamaEndpoint="${Models__BatchOllamaEndpoint:-http://localhost:11436}"
export Models__BatchOllamaNumThread="${Models__BatchOllamaNumThread:-${OLLAMA_BATCH_THREADS:-12}}"

# One corpus and one collection per domain: billing (data/ → maf_chunks), portfolio (data-portfolio/ → maf_portfolio_chunks)
# and the codebase (the repository → maf_code_chunks).
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

index_domain "$COLLECTION" "${Indexing__CorpusRoot:-$ROOT/data}" "${Qdrant__MetaCollection:-maf_meta}"
index_domain maf_portfolio_chunks "$ROOT/data-portfolio" maf_portfolio_meta
# The codebase: the repository itself, cut by structure, in embedding tokens (see CODE_ENV in the Makefile).
Indexing__Layout=repository Indexing__MaxChunkTokens=1024 Indexing__Bm25Tokenizer=code \
  index_domain maf_code_chunks "$ROOT" maf_code_meta

# The graph: billing relationships and the code graph (make graph). Counted through cypher-shell in the neo4j container,
# so the host needs no Neo4j client.
nodes=$(MAF_LAB_REPO="${MAF_LAB_REPO:-$ROOT}" docker compose -f "$ROOT/compose/docker-compose.yml" exec -T neo4j \
  cypher-shell -u neo4j -p "${NEO4J_PASSWORD:-maf-lab-dev-graph}" --format plain "MATCH (n) RETURN count(n) AS nodes" 2>/dev/null \
  | tail -1 | tr -dc '0-9' || true)
if [[ "${nodes:-0}" -gt 0 ]]; then
  echo "✓ graph present ($nodes nodes) — skipping the graph build"
else
  echo "… graph is empty — building it (billing relationships and the code graph)"
  Neo4j__Uri="${Neo4j__Uri:-bolt://localhost:7687}" Neo4j__Password="${NEO4J_PASSWORD:-maf-lab-dev-graph}" \
    "$DOTNET" run --project "$ROOT/src/Maf.Lab.Indexing" -- graph
fi

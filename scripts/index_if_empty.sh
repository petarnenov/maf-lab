#!/usr/bin/env bash
# Indexes each domain's corpus only when its Qdrant collection is missing or empty.
# Env: QDRANT_URL (http://localhost:6333), Qdrant__Collection (maf_chunks), DOTNET (dotnet),
#      Models__OllamaEndpoint (http://localhost:11435 — the compose Ollama).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
QDRANT_URL="${QDRANT_URL:-http://localhost:6333}"
COLLECTION="${Qdrant__Collection:-maf_chunks}"
DOTNET="${DOTNET:-dotnet}"
export Models__OllamaEndpoint="${Models__OllamaEndpoint:-http://localhost:11435}"

# One corpus and one collection per domain: billing (data/ → maf_chunks) and portfolio (data-portfolio/ → maf_portfolio_chunks).
index_domain() {
  local collection="$1" corpus="$2" meta="$3"
  local points
  points=$(curl -sf "$QDRANT_URL/collections/$collection" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"].get("points_count") or 0)' 2>/dev/null || echo 0)
  if [[ "$points" -gt 0 ]]; then
    echo "✓ index present ($collection: $points chunks) — skipping indexing"
    return 0
  fi
  echo "… index $collection is empty — indexing $corpus (embeddings via $Models__OllamaEndpoint)"
  Indexing__CorpusRoot="$corpus" Qdrant__Collection="$collection" Qdrant__MetaCollection="$meta" \
    "$DOTNET" run --project "$ROOT/src/Maf.Lab.Indexing" -- index
}

index_domain "$COLLECTION" "${Indexing__CorpusRoot:-$ROOT/data}" "${Qdrant__MetaCollection:-maf_meta}"
index_domain maf_portfolio_chunks "$ROOT/data-portfolio" maf_portfolio_meta

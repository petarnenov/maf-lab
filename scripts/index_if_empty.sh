#!/usr/bin/env bash
# Indexes the sample corpus only when the Qdrant collection is missing or empty.
# Env: QDRANT_URL (http://localhost:6333), Qdrant__Collection (maf_chunks), DOTNET (dotnet),
#      Models__OllamaEndpoint (http://localhost:11435 — the compose Ollama).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
QDRANT_URL="${QDRANT_URL:-http://localhost:6333}"
COLLECTION="${Qdrant__Collection:-maf_chunks}"
DOTNET="${DOTNET:-dotnet}"
export Models__OllamaEndpoint="${Models__OllamaEndpoint:-http://localhost:11435}"

points=$(curl -sf "$QDRANT_URL/collections/$COLLECTION" | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"].get("points_count") or 0)' 2>/dev/null || echo 0)
if [[ "$points" -gt 0 ]]; then
  echo "✓ index present ($COLLECTION: $points chunks) — skipping indexing"
  exit 0
fi
echo "… index $COLLECTION is empty — indexing the corpus (embeddings via $Models__OllamaEndpoint)"
"$DOTNET" run --project "$ROOT/src/Maf.Lab.Indexing" -- index

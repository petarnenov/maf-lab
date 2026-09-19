# maf-lab

A learning lab: a RAG-backed assistant for a TAMP billing domain. Retrieval is a **tool** (`search_documents`) on an
**MCP server** over a **multi-tenant Qdrant** collection, consumed by a **Microsoft Agent Framework** agent, with a React
chat UI, an eval harness and tested prompt-injection defences.

- Stack, layout and hard conventions: [`openspec/project.md`](openspec/project.md)
- Why things are the way they are, and every pinned version: [`DECISIONS.md`](DECISIONS.md)
- Current behaviour (specs): [`openspec/specs`](openspec/specs); changes that built it:
  [`add-day3-retrieval`](openspec/changes/archive/2026-09-19-add-day3-retrieval), `add-load-balancer`
- HTTP contract between API and web: [`docs/http-api.md`](docs/http-api.md)

```
                        ┌──────────── lb (nginx, http://localhost:7171) ─────────────┐
                        │  /            /api/*, /dev/* (SSE)          /mcp           │
                        ▼                    ▼                         ▼             │
                  web (static SPA)    api ×2 (Agent Framework) ──▶ lb/mcp ──▶ mcp-retrieval ×2
                                           │  SQLite (WAL, shared volume):          │
                                           │  conversations, turns, feedback,       ▼
                                           │  audit, admin jobs              Qdrant (:6333/6334)
                                           └──── chat: Ollama Cloud · embeddings: Ollama (:11435)
```

Everything user- and agent-facing goes through **one entry point on port 7171**. api and mcp-retrieval run two replicas
each (`X-Instance` response header shows which one answered); only Qdrant and Ollama are published besides 7171.

## Quick start (everything in Docker)

```bash
# Reuse models a host Ollama already pulled (optional; otherwise ollama-init downloads ~3 GB):
export OLLAMA_MODELS_DIR=$HOME/.ollama
docker compose -f compose/docker-compose.yml up -d --build

# Index the sample corpus into the compose Qdrant (uses Ollama on 11435 for embeddings):
Models__OllamaEndpoint=http://localhost:11435 dotnet run --project src/Maf.Lab.Indexing

open http://localhost:7171      # pick a persona, ask "What is the procedure when a fee schedule is missing?"
scripts/verify_lb.sh            # checks routing, closed ports, balancing, SSE, MCP, replica failure, admin jobs

# More replicas:
docker compose -f compose/docker-compose.yml up -d --scale api=3 --scale mcp-retrieval=3
```

## Local development (without the balancer)

Running the services with `dotnet run` / `npm run dev` bypasses the balancer: web on :5174 (Vite proxies `/api` and
`/dev` to :5080), api on :5080, MCP on :5090. Stop the compose app services first (`docker compose ... stop api
mcp-retrieval web lb`) — the infrastructure (Qdrant, Ollama) can stay up.

Prerequisites: .NET SDK 10.0.401 (`global.json`), Node 24, Docker, Ollama (host or compose).

```bash
docker compose -f compose/docker-compose.yml up -d qdrant     # vector store only
ollama pull nomic-embed-text && ollama pull all-minilm && ollama pull qwen3:4b

dotnet run --project src/Maf.Lab.Indexing                     # index data/ (index | drift | status | migrate --to dense_v2)
dotnet run --project src/Maf.Lab.Retrieval                    # MCP server on :5090
dotnet run --project src/Maf.Lab.Api                          # agent host on :5080
cd web && npm install && npm run dev                          # UI on :5174
```

VS Code: the compound launch **"api + web (with mcp-retrieval)"** starts all three; tasks cover `compose up`, `index`,
`eval` and tests.

Switch the model provider by configuration only, e.g. `Models__Provider=openai Models__OpenAIApiKey=… Models__ChatModel=gpt-4.1-mini`
(Azure OpenAI: also set `Models__OpenAIEndpoint=https://<resource>.openai.azure.com/openai/v1/`). Embedding models live
under `Models:Embeddings` (one entry per Qdrant named vector).

## Tests

```bash
dotnet test --solution maf-lab.sln      # unit + integration (Testcontainers starts qdrant/qdrant:v1.19.1; Docker required)
cd web && npm test -- --run && npm run lint && npm run build
```

Tests never call a model: they use a deterministic feature-hashing embedder and a scripted chat client. Evals are
separate and do call the model.

## Evals — when you must run them

```bash
dotnet run --project src/Maf.Lab.Eval -- --suite all                 # selection, retrieval, generation, injection
dotnet run --project src/Maf.Lab.Eval -- --suite retrieval --rerank   # add the rerank variant
dotnet run --project src/Maf.Lab.Eval -- --import-feedback --suite retrieval
Evals__McpEndpoint=http://localhost:7171/mcp dotnet run --project src/Maf.Lab.Eval -- --suite selection   # through the stack
```

Evals run **on demand**, not on every commit. They are **required** before merging any change to:

- the **system prompt** (`src/Maf.Lab.Api/Prompts/*`) → `selection`, `generation`, `injection`
- a **tool description or schema** (`src/Maf.Lab.Retrieval/Tools/*`) → `selection`
- the **model** (chat or embedding, `Models:*`) → `all`
- the **tool set** (adding/removing a tool) → `selection`, `injection`
- the **chunking or retrieval configuration** (chunkers, `Indexing:*`, `Retrieval:*`, BM25) → `retrieval`, `generation`

Datasets are JSONL under `evals/`; reports land in `evals/reports/` (JSON for the `/evals` page, Markdown for humans).
Thresholds are configuration (`src/Maf.Lab.Eval/eval.json` → `Evals:Thresholds`); the command exits non-zero when a
suite falls below them. Labeled production feedback (UI → `/admin/feedback`) is appended to the datasets, so the next
run includes it. The contextual-retrieval variant needs a second index:

```bash
Qdrant__Collection=maf_chunks_ctx Qdrant__MetaCollection=maf_chunks_ctx_meta \
  dotnet run --project src/Maf.Lab.Indexing -- index --contextual on
dotnet run --project src/Maf.Lab.Eval -- --suite retrieval --contextual
```

## Non-negotiables

Tenant comes from the token only · one tenant-scoped query path · tool results are DTOs · no message content in logs ·
version moves update `DECISIONS.md`. See [`CLAUDE.md`](CLAUDE.md).

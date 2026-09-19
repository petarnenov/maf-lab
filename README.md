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

## Quick start

```bash
export OLLAMA_API_KEY=…    # chat runs on Ollama Cloud (gpt-oss:120b); the key is only read from the environment
make                       # doctor-lite → build → start → wait until healthy → index if empty → http://localhost:7171
make help                  # every target
```

| Target | What it does |
|---|---|
| `make` / `make up` | Start the stack (`API_REPLICAS=3 MCP_REPLICAS=3` to scale), wait until healthy, reload the balancer |
| `make down` / `restart` / `ps` / `logs` | Stop (volumes kept), restart, status, follow logs (`SERVICE=api`) |
| `make index` / `reindex` / `drift` / `migrate` | Indexing CLI against the compose Qdrant + Ollama (`TO=dense_v2`) |
| `make test` / `test-dotnet` / `test-web` / `lint` | Test suites and linters |
| `make verify` | 17 checks through the load balancer (routing, ports, balancing, SSE, MCP, failover, jobs) |
| `make eval` / `eval-selection` / … | Evals against the stack's MCP (`SUITE=all`) |
| `make dev` | Run mcp/api/web locally without Docker (infra stays in compose); Ctrl-C stops |
| `make doctor` | Check Docker, .NET SDK, Node, make, `OLLAMA_API_KEY` (value never printed) |
| `make clean` | Remove the stack **with volumes** and build outputs (asks; `FORCE=1` to skip) |

## Local development (without the balancer)

`make dev` bypasses the balancer: web on :5174 (Vite proxies `/api` and `/dev` to :5080), api on :5080, MCP on :5090,
with Qdrant and Ollama from compose (the compose app services are stopped first). The manual equivalent:

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
make test        # dotnet test --solution maf-lab.sln (Testcontainers starts qdrant/qdrant:v1.19.1) + web Vitest
make lint        # .NET build with warnings as errors + ESLint/Prettier
```

Tests never call a model: they use a deterministic feature-hashing embedder and a scripted chat client. Evals are
separate and do call the model.

## Evals — when you must run them

```bash
make eval                     # all suites against the running stack's MCP
make eval-selection           # or eval-retrieval / eval-generation / eval-injection
dotnet run --project src/Maf.Lab.Eval -- --suite retrieval --rerank   # extra flags: use the CLI directly
dotnet run --project src/Maf.Lab.Eval -- --import-feedback --suite retrieval
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

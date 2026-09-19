# maf-lab

[![CI](https://github.com/petarnenov/maf-lab/actions/workflows/ci.yml/badge.svg)](https://github.com/petarnenov/maf-lab/actions/workflows/ci.yml)

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

The same picture, drawn in [`docs/topology.drawio`](docs/topology.drawio) and **live**, is at
[`/topology`](http://localhost:7171/topology): each box carries the state of that service — healthy, degraded or
unreachable — its replicas by name, and the facts that explain the lab's behaviour (chunks in the index, models,
tools offered). Edit the diagram in draw.io (save it *uncompressed*) and the page follows; a service that exists in
the report but not in the drawing fails the test suite.

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

## Chat history

The left sidebar of `/chat` lists your own conversations, most recent activity first. You can search titles, questions
and answers. Open a conversation to restore every turn exactly as it looked (answers, tool cards, sources, feedback,
and the behind-the-scenes trace with time travel while it is kept), then continue it. The active conversation is in
the URL (`/chat/{id}`), so a reload reopens it. Rename or delete from the item's menu. Delete hides the conversation
and stops it being continued; its turns stay for the review queue and evals.

## Behind the scenes

`/chat` shows the conversation on the left and a live **behind-the-scenes monitor** on the right: every step of the turn
as it happens. Tabs:
- **Timeline:** a waterfall of every step.
- **Model:** each request (messages, tools, tool mode) and response (text, tool calls, tokens, latency).
- **Retrieval:** tenant scope, settings, BM25 terms with IDF, and the dense, sparse and fused candidates side by side.
- **MCP:** raw arguments and results, plus the api and mcp replica that served them.
- **Prompt & memory:** system prompt, tool schemas and the history window.

**Intent:** before the first model call the question is classified, which decides whether the turn is *forced* to call
`search_documents`. English rules decide first, for free; a question they do not recognise — anything in another
language, for instance — is classified by a model (`Agent:IntentModel`, default the chat model, budget
`Agent:IntentTimeoutSeconds`, 0 disables it). The intent event names the stage, so "Intent Procedural (model, 959 ms)"
is a turn that Bulgarian rules never matched. A timeout, a failure or an answer that is not one of the five intents
leaves the turn unforced, exactly as before the classifier existed.

**Time travel:** scrub, step (←/→), jump (Home/End) or replay (Space; 1×–10×, long waits compressed) through any
turn. Every tab shows the state as of the chosen step, and the chat rewinds with it: the answer text, tool cards and
sources appear as they were at that moment. While a turn streams, the monitor follows it; drag back to pause and use
"Back to live" to catch up.

Click an earlier answer to reopen its stored trace (kept 7 days). Reviewers can open a trace from `/admin/feedback`. The
event format is in [`docs/trace-events.md`](docs/trace-events.md). Retrieval internals come from the MCP server in the
tool result `_meta`, which the model never sees.

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

## Continuous integration

GitHub Actions ([`.github/workflows`](.github/workflows)) — `make ci` runs the same checks locally.

| Workflow | Trigger | What runs |
|---|---|---|
| **CI** (`ci.yml`) | every push and pull request | `specs` (OpenSpec strict validation) · `dotnet` (build with warnings as errors, unit + Testcontainers integration tests) · `web` (lint, Vitest, build) · `e2e` (full stack behind the balancer on :7171, corpus indexed, `make verify`) |
| **Evals** (`evals.yml`) | manual (*Actions → Evals → Run workflow*, choose a suite) | real embeddings in compose Ollama + chat on Ollama Cloud (`OLLAMA_API_KEY` repository secret); reports uploaded as an artifact |

The e2e job needs **no model and no secret**, so it also runs for pull requests from forks. `CI_MODE=1` replaces the
`ollama` service with a deterministic Ollama-compatible stub (`compose/ollama-stub`): hash-based embeddings and a
scripted, streamed chat answer. Forced retrieval still calls `search_documents` over MCP, so tool calls, SSE, sources,
tenancy, failover and admin jobs are exercised for real. Try it locally: `make ci-e2e` (indexing takes about 15 s with the stub).

```bash
gh workflow run evals.yml -f suite=selection   # dispatch evals from the CLI
gh run watch                                   # follow it
```

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

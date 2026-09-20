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

## Asking in another language

The corpus is English. A question written in another language is translated into the corpus language *before* it is
embedded and before BM25 encodes it, so both halves of hybrid retrieval work on the same vocabulary as the index;
the answer still comes back in the language of the question. The monitor's Retrieval tab shows both texts, and the
retrieval eval reports recall per language. Measured over the eval set, Bulgarian recall@5 went from **0.21 to
0.68** while English stayed at 0.69. Switch it off with `Retrieval__NormalizeQueryLanguage=false`; the corpus
language is `Retrieval__CorpusLanguage` (default `en`).

## Another agent talking to this one (A2A)

The assistant is also an **[A2A 1.0](https://a2a-protocol.org) agent**, so another system can work with it without
a person in the loop. It advertises itself at `http://localhost:7171/.well-known/agent-card.json` — a signed card
naming its skills, its transports (JSON-RPC and HTTP+JSON) and how to authenticate — and answers on `/a2a`.

A partner is a **system, not a user**: its token's audience is the A2A endpoint, it carries no user identity, and
the firms it may see come from the server's `A2A:Partners` registration rather than from anything it sends. Ask
about a firm outside that set and you get one fixed sentence and no data — not the run, not whether it exists.

It can ask about a billing run, ask anything else (answered by the same agent and the same MCP tools the chat UI
uses), or **start a billing run** — which comes back as a task it can follow, resume when the agent asks for a
missing period, resubscribe to after a dropped connection, cancel, or be notified about through a webhook. That
run is **simulated**: it walks the real lifecycle over the seeded runs and bills nobody. The card says so, the
final artifact says so (`"simulated": true`), and so does this paragraph.

Two clients prove it from outside: `python3 scripts/a2a_probe.py` speaks the 1.0 wire format and imports no A2A
library at all, and `dotnet run --project tools/Maf.Lab.A2AProbe` builds an agent from nothing but the card using
the Agent Framework's A2A client. The preview SDK underneath does not yet speak 1.0 on the wire; every difference
and what is done about it is in `DECISIONS.md`.

## A second agent it consults (A2A, the other way round)

The lab runs a **second agent** of its own: a compliance reviewer, in its own container behind the same entry
point at `/compliance`, with its own card, its own audience and its own credentials. Its single skill is to review
a proposed fee adjustment and return a verdict — and it behaves like a real dependency rather than a function
call: it takes tens of seconds, reports progress while it works, and about one review in five stops and asks for
the advisor's justification before deciding.

The assistant consults it as a **sub-agent** through the Agent Framework's A2A client: it fetches the reviewer's
card, turns it into an agent, and authenticates **as itself** — a user's token is never forwarded, and the
reviewer never learns who asked. Every way the consultation can end is a value the caller must handle: a verdict,
a question back, a timeout that keeps the task id so the answer can be collected later, an unreachable agent, or a
failure. Each one is written to the same audit record as everything else, with no message content.

That verdict is **simulated** — a threshold and a stopwatch. It binds nobody, and the card, the artifact and this
paragraph all say so. `dotnet run --project tools/Maf.Lab.A2AProbe` drives both agents from outside, using nothing
but their cards.

## The first thing it can change

Everything else the assistant does can be undone by closing the tab. `propose_fee_adjustment` is the exception —
and it cannot use itself. Asking for a fee to be adjusted gets you a **proposal**, never a change: the tool
returns the account, its current fee, the amount, the fee that would result and the period it would affect, and
the turn ends there. The adjustment happens only when the advisor answers, and only then.

What the advisor confirms is what executes. The proposal travels back as an opaque, signed token, and the second
call runs *that* rather than whatever the model repeated in the meantime — point it at another account and the
original one is still what moves. Approving twice charges once: the ledger's unique key refuses the second
application and reports the first one's result. Nothing is stored until something is actually applied; the seeded
accounts stay read-only, and an account's current fee is the seed plus its applied adjustments.

Above a configured amount (`FeeAdjustments:ReviewAboveAmount`, 500 by default) the compliance reviewer sees it
before the advisor does. Its refusal ends the flow, its question is put to the advisor and answered under the
same review, and a timeout, an unreachable reviewer or a failure ends the flow with an explanation — never with a
confirmation, and never with a write. Its verdict is treated as coming from a system this one does not control:
checked against the account and adjustment that were sent, and handed to the model as data. An instruction
embedded in it changes nothing, because nothing reads it for instructions.

Every step — proposed, reviewed, confirmed, rejected, applied — is in the audit record under `fee.adjustment`,
naming the person who acted and the account, and never the reason they gave.

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
- **query normalisation** (`Retrieval:NormalizeQueryLanguage`, `Retrieval:CorpusLanguage`, the translation model) → `retrieval`

### Not getting worse

The thresholds answer "is this usable at all"; the **baseline** answers "is this worse than it was".
`evals/baseline.json` is committed and records the metrics this repository has accepted, per suite and variant.
Every run compares against it and fails when a metric drops by more than the tolerance, naming what moved:

```
✗ REGRESSION retrieval/hybrid mrr: 0.9 → 0.647 (-0.253)
↑ improved retrieval/hybrid recall@5: 0.5 → 0.687 (+0.187)
· within tolerance retrieval/hybrid recall@5:bg: 0.681 → 0.66 (-0.021)
make eval-accept        # run the suites and accept their metrics as the new baseline (then commit it)
```

A run never moves the baseline by itself, and a run below its thresholds is refused rather than blessed. A metric
the baseline does not mention is reported as *new* and one it mentions but the run did not produce as *missing* —
both are how a rename silently switches the gate off. The tolerance is `Evals:RegressionTolerance` (0.02) with a
per-suite override; `retrieval` uses 0.03 because a non-English query is translated by a live model, and
`recall@5:bg` was measured alternating between 0.660 and 0.681 across four runs while the English metric never
moved. `/evals` plots any metric across past runs with the baseline marked.

Datasets are JSONL under `evals/`; reports land in `evals/reports/` (JSON for the `/evals` page, Markdown for humans).
Thresholds are configuration (`src/Maf.Lab.Eval/eval.json` → `Evals:Thresholds`); the command exits non-zero when a
suite falls below them. Labeled production feedback (UI → `/admin/feedback`) is appended to the datasets, so the next
run includes it. The contextual-retrieval variant needs a second index:

```bash
Qdrant__Collection=maf_chunks_ctx Qdrant__MetaCollection=maf_chunks_ctx_meta \
  dotnet run --project src/Maf.Lab.Indexing -- index --contextual on
dotnet run --project src/Maf.Lab.Eval -- --suite retrieval --contextual
```

## Compliance

Every audited action — a tool call, a conversation deletion, a compliance export — is one row in a **single ordered
record**, carrying identifiers only and never message content. Each row is chained: its digest covers its own fields
and the previous row's digest, so a changed or removed row can be detected and *named*.

**`/admin/compliance`** (FIRM_ADMIN) shows it: whether the chain is intact — in words, with how many records were
checked and how many predate it — or, when it is broken, which record broke it and that everything before it is
unaffected. Below that, the firm's actions newest first, filterable by person, kind and period. The same endpoints
serve a script:

```bash
GET /api/admin/compliance/verify                      # intact? how many checked? where does it break?
GET /api/admin/compliance/actions?userId=&kind=…      # the record, paged
GET /api/admin/compliance/export?from=&to=[&userId=]  # the package, with a manifest
```

A FIRM_ADMIN can hand an authorised person a package for a period: their firm's conversations, turns and actions,
including deleted conversations marked as deleted. Adding `userId` narrows it to one person, for a data subject
request. The firm comes from the token, so no parameter reaches another firm. The manifest carries who produced it,
when, the counts, the audit chain head, and a digest over a canonical rendering of the content — documented in
[`docs/http-api.md`](docs/http-api.md) so a recipient can recompute it in any language.

**What this does not promise.** A hash chain is evidence of tampering, not protection from it: whoever can write the
database can recompute the whole chain. Real immutability needs storage the application cannot rewrite (a WORM
bucket, an external log service), which is a deployment decision. And the dev token issuer still decides who "adam"
is — the chain proves what was recorded, not that the recorded person is who they claim.

## Non-negotiables

Tenant comes from the token only · one tenant-scoped query path · tool results are DTOs · no message content in logs ·
version moves update `DECISIONS.md`. See [`CLAUDE.md`](CLAUDE.md).

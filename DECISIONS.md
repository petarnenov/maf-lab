# DECISIONS

Pinned versions and the architectural decisions of maf-lab. **If a version moves, update this file in the same commit.**

## 1. Pinned versions (2026-09-19)

### Toolchain and images

| Component | Version | Where pinned |
|---|---|---|
| .NET SDK | 10.0.401 (LTS band) | `global.json` (`rollForward: latestPatch`) |
| .NET runtime / ASP.NET Core | 10.0.12 | Docker `mcr.microsoft.com/dotnet/aspnet:10.0.12-noble` |
| .NET SDK image | `mcr.microsoft.com/dotnet/sdk:10.0.401-noble` | `src/*/Dockerfile` |
| Test runner | Microsoft.Testing.Platform (required by `dotnet test` on .NET 10 SDK) | `global.json` `test.runner` |
| Qdrant | `qdrant/qdrant:v1.19.1` (compose and Testcontainers) | `compose/docker-compose.yml`, `tests/.../QdrantFixture.cs` |
| Ollama | `ollama/ollama:0.34.2` | `compose/docker-compose.yml` |
| Node (build) | `node:24.21.0-alpine` (local dev: Node 24.11.0) | `web/Dockerfile` |
| nginx (web runtime and load balancer) | `nginx:1.30.5-alpine` | `web/Dockerfile`, `compose/docker-compose.yml` (`lb`) |

### Models (Ollama)

| Role | Model | Notes |
|---|---|---|
| Dense embedding `dense_v1` | `nomic-embed-text` (768-d) | `search_document:` / `search_query:` prefixes |
| Dense embedding `dense_v2` | `all-minilm` (384-d) | migration target |
| Chat / agent / judge / rerank / contextual | **`gpt-oss:120b` on Ollama Cloud** (`https://ollama.com`) | key from env `OLLAMA_API_KEY`; thinking disabled (see §9) |
| Local chat fallback | `qwen3:4b` | `Models__ChatEndpoint=http://localhost:11434 Models__ChatModel=qwen3:4b` |

### NuGet (central package management, `Directory.Packages.props`)

| Package | Version |
|---|---|
| Microsoft.Agents.AI | 1.22.0 |
| Microsoft.Extensions.AI / .Abstractions / .OpenAI | 10.10.0 |
| OllamaSharp | 5.4.30 |
| ModelContextProtocol / ModelContextProtocol.AspNetCore | 2.2.0 |
| Qdrant.Client | 1.19.0 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 |
| System.IdentityModel.Tokens.Jwt | 8.23.0 |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.12 |
| Microsoft.Extensions.Hosting | 10.0.12 |
| Microsoft.ML.Tokenizers (+ Data.O200kBase) | 2.0.0 |
| Markdig | 1.3.2 |
| Microsoft.Bcl.Memory (transitive security pin, GHSA-73j8-2gch-69rq) | 10.0.12 |
| xunit.v3 / xunit.runner.visualstudio | 4.0.1 / 4.0.0 |
| Microsoft.NET.Test.Sdk | 18.10.1 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 |
| Testcontainers.Qdrant | 4.15.0 |
| Mono.Cecil | 0.11.6 |

`CentralPackageTransitivePinningEnabled` is on, so transitive pins in `Directory.Packages.props` apply.

### npm (`web/package.json`, exact versions, `save-exact`)

react / react-dom 19.3.0 · react-router 8.4.0 · @tanstack/react-query 5.103.1 · vite 8.3.0 · @vitejs/plugin-react 6.1.1 ·
vitest 5.0.1 · jsdom 29.1.1 · @testing-library/react 16.3.3 · @testing-library/dom 10.4.2 · @testing-library/jest-dom 7.0.1 ·
@testing-library/user-event 14.6.7 · typescript 6.0.3 · eslint 10.11.0 · @eslint/js 10.0.1 · typescript-eslint 8.70.0 ·
eslint-plugin-react-hooks 7.1.1 · eslint-plugin-react-refresh 0.5.7 · globals 17.12.0 · prettier 3.9.8 · @types/react(-dom) 19.3.0 · @types/node 24.13.6

- **TypeScript 6.0.3, not 7.x**: typescript-eslint 8.70 supports `<6.1`.
- **jsdom 29.1.1, not 30.x**: jsdom 30 requires Node ≥ 24.15; the dev machine runs 24.11.

## 2. Multitenancy mode

**Payload-based multitenancy in one collection** (`maf_chunks`):

- `tenant_id` keyword payload index with `is_tenant=true` (co-locates each tenant's points).
- HNSW `m=0`, `payload_m=16`: no global graph; each tenant gets its own sub-graph, so a filtered search never
  walks another tenant's neighbourhood and small tenants do not suffer post-filter shrinkage
  (acceptance test: firm-c with 50 docs gets its top-10 while the global top-10 is entirely firm-b).
- Payload indexes on `source_type`, `doc_id`, `model_version` (keyword) and `updated_at` (datetime).
- Tenant key values: a firm id (`firm-a` …) or `shared`. A principal reads `[own firm, shared]`.

**Enforcement is structural.** Only `TenantScopedSearch.QueryAsync(Principal, …)` reads chunk content for a caller,
and it attaches the tenant filter to every prefetch branch and the outer query. Writes/admin reads go through
`TenantScopedMaintenance`, every method scoped to exactly one `TenantId` taken from the corpus layout or a
FIRM_ADMIN principal. `QueryPathEnumerationTests` scans the IL of every `Maf.Lab.*` assembly and fails if any other
type calls a Qdrant data-plane method (Bm25Store is allowed `Retrieve/Upsert` on the meta collection only), and a
fixture proves the scanner catches a rogue call.

### When to move to tiered multitenancy (follow-up, out of scope)

Replace with Qdrant tiered multitenancy — a shared **fallback shard** for small tenants plus **dedicated shards**
(custom sharding, `shard_key` = tenant) for large ones, with **tenant promotion** moving a tenant's points to its own
shard — when any of these triggers fires:

1. one tenant holds more than ~20 % of all points (firm-b is ~80 % of this lab's corpus, so a real deployment with
   this shape would promote it), or
2. p95 search latency for any tenant breaches its SLO because of neighbour volume, or
3. a tenant needs isolation guarantees payload filtering cannot give (separate retention, per-tenant snapshots/restore,
   noisy-neighbour protection on writes).

Promotion plan: create the tenant's shard key, copy its points by filter, switch its queries to `shard_key`, delete from
the fallback shard. Query code would add a shard-key selector inside `TenantScopedSearch` only.

## 3. Sparse encoder

**In-repo BM25** (`Maf.Lab.Retrieval.Sparse`), no external service:

- Tokenizer: NFKC, lowercase, split on non letter/digit, English stopwords, keeps numeric ids (`4417`).
- Vocabulary: stable term → uint id, never renumbered; persisted with document frequencies and length stats as one
  point in the `maf_meta` collection; the MCP server caches it for 30 s.
- Document vectors store only the TF-saturation term `tf·(k1+1)/(tf+k1·(1−b+b·dl/avgdl))` (k1 = 1.2, b = 0.75);
  **query vectors carry IDF**, so dot product = BM25 and IDF can be refreshed without re-encoding documents.
- Statistics are computed from the **whole corpus** on every indexing run, independent of which tenants are written.
  Known trade-off: document frequencies are aggregated across tenants, so term rarity is a (weak) cross-tenant
  statistic. Mitigation if required: per-tenant IDF tables keyed by tenant.
- BM25 encodes `section_path + text`; the contextual-retrieval sentence enriches only the dense embedding.
- Rejected alternative: Qdrant/FastEmbed `bm25` or SPLADE — the project requires no external encoder service and
  the learning goal is to own the encoder.

## 4. Hybrid query

Query API with two prefetches (dense, sparse), each carrying the tenant filter, limit `max(MinPrefetch=50, 5 × k)`,
fused server-side with **RRF** (default) or **DBSF** (`Retrieval:Fusion=dbsf`). `Retrieval:Mode` = `hybrid | dense |
sparse`. Optional rerank (`Retrieval:RerankEnabled`) is an LLM listwise reranker standing in for a cross-encoder; on any
failure it degrades to the fused order and logs only the error type. Its input is the tenant-scoped candidate list.

## 5. Indexing identity and re-indexing

- `doc_id = "{tenant}/{path within tenant}"` and `chunk_id = "{doc_id}#{section-slug}[-n]"`: stable across runs and
  readable in eval datasets (changed from the design's SHA-256 doc id for labelability). Point id = first 16 bytes of
  SHA-256(chunk_id), so re-upserts overwrite.
- Tenant comes from the first path segment; anything else (e.g. `data/unowned/…`) is rejected and reported, never
  defaulted to `shared`. Tenants to (re)index are the tenant **folders** present in the layout, so a tenant whose last
  document was removed still gets its stale chunks deleted.
- **Re-index = upsert new points, then delete every other point of that `doc_id`.** This deviates from the proposal's
  wording ("delete before upsert") on purpose: upsert-then-delete never leaves a window where the document is missing
  from search, and the outcome — exactly one version of each chunk — is the same (tested).
- Unchanged documents (same content hash, `updated_at` and `model_version`) are skipped.

## 6. Embedding-model migration

- **Qdrant cannot add a named vector to an existing collection** (verified on 1.19.1: `PATCH /collections/x` with a new
  vector name → `Not existing vector name error`). Both `dense_v1` and `dense_v2` are therefore provisioned at creation;
  a point may lack `dense_v2` until migrated. Adding a third model means provisioning it up front or re-creating the
  collection (bootstrap fails loudly if a configured vector is missing).
- `migrate --to dense_v2` scrolls batches with `model_version != target`, embeds, and writes with
  **`UpdateVectors` + filter (conditional update)** so only points still on an old model are touched, then sets
  `model_version`. A crash between the two steps just re-embeds those points next run. Restartable and idempotent
  (tested by killing after two batches and re-running: no duplicates, queries succeed throughout).
- `Retrieval:DenseVector` selects the queried vector; `Indexing:DenseVector` the one new documents are written with.
  Switch both after a migration; rollback = switch back to `dense_v1`.

## 7. MCP server vs spec 2026-07-28

ModelContextProtocol C# SDK **2.2.0** implements the 2026-07-28 revision natively for what this change needs:

- Stateless Streamable HTTP is the SDK default (`HttpServerSessionMode.Stateless`, SEP-2567): no `Mcp-Session-Id`,
  GET/DELETE/SSE endpoints disabled. Verified by test (`client.SessionId == null`, repeated independent calls).
- Per-request `_meta` (`io.modelcontextprotocol/protocolVersion`, `…/clientCapabilities`) and the `Mcp-Method` header
  are enforced by the server and sent by the SDK client.
- Tool annotations, `outputSchema` / `structuredContent` and `isError` results are all first-class.

**Gaps found: none for this change.** Not exercised here (out of scope): MRTR `input_required` for write tools.

## 8. Agent (Microsoft Agent Framework 1.22) notes and gaps

- The agent is a `ChatClientAgent` over `IChatClient` (OllamaSharp; OpenAI / Azure OpenAI v1 endpoint by
  `Models:Provider=openai` + `Models:OpenAIEndpoint`). MCP tools come from `McpClient.ListToolsAsync()` (they are
  `AIFunction`s) with the **user's bearer token forwarded**, so the MCP server derives the tenant itself.
- **Gap:** agent function-invocation middleware only runs for tools that exist; a call to a non-existent tool
  (`send_email`) never reaches it. Handled by watching `FunctionCallContent` in the stream: the attempt is audited as
  `unknown_tool`, surfaced as an error tool card, and M.E.AI's `FunctionInvokingChatClient` returns a "not found" error
  to the model.
- Forced retrieval uses `ChatToolMode.RequireSpecific("search_documents")` for the turn; `FunctionInvokingChatClient`
  resets a required tool mode after the first iteration, so forcing cannot loop and the next turn is unaffected
  (tested).
- Conversation memory is a custom `ChatHistoryProvider` over SQLite. Only user and final assistant text are stored —
  tool calls and tool data are not replayed into later turns (smaller window, and poisoned tool data does not persist).
- There is no built-in per-turn `MaximumIterations` knob on `ChatClientAgentOptions`; the framework default applies.

## 9. Models and providers

- **Chat runs on Ollama Cloud (2026-09-19).** Local `qwen3:4b` on this Intel Mac is CPU-only (~30 s per agent turn,
  ~20 min for the selection suite); `gpt-oss:120b` on Ollama Cloud answers in ~1 s and is the only cloud model in the
  account's free tier that passed a tool-calling probe (others return "not included in your free usage").
  `Models:ChatEndpoint` (default `https://ollama.com`) is used for chat only; **embeddings stay local**
  (`Models:OllamaEndpoint`) because Ollama Cloud serves no embedding models. The bearer key is read at client
  creation from the environment variable named by `Models:ChatApiKeyEnvironmentVariable` (default `OLLAMA_API_KEY`);
  it is never written to config files, and a remote endpoint without the variable fails fast naming the variable.
  Compose passes `OLLAMA_API_KEY` through from the host environment.
- **Ollama ignores `tool_choice`**, so `ChatToolMode.RequireSpecific` is silently dropped (observed: "why did run 4417
  fail" skipped `search_documents`). `RequiredToolModeChatClient` issues the forced `search_documents` call on the
  model's behalf with the user's question as the query, for the first iteration only (`Agent:EmulateRequiredToolMode`,
  default true). Selection recall went from 0.905 to 1.0.

- `Microsoft.Extensions.AI.Ollama` is discontinued (last: 9.7.0-preview); **OllamaSharp** implements both
  `IChatClient` and `IEmbeddingGenerator`.
- qwen3 emits reasoning tokens by default; `ChatOptions.RawRepresentationFactory` sends `think: false`
  (`Models:DisableThinking`).
- Ollama in Docker on macOS runs on CPU only; compose can reuse host-pulled models with `OLLAMA_MODELS_DIR=$HOME/.ollama`.
  Compose maps Ollama to host port 11435 so it does not clash with a host Ollama on 11434.

## 10. Prompt-injection defences

Tool results reach the model inside `<tool_data tool="…">` blocks with a data-not-instructions notice; closing tags
inside payloads are neutralised. No side-effecting tools exist. Billing records' free-text `note` is not part of any
output contract (the DTOs have no such field). The injection eval uses canaries (`NW-CANARY-7731-…`,
`ACME-CANARY-4410`, `CONTOSO-CANARY-2290`) and poisoned documents in shared, firm-a, firm-b and firm-c.

## 11. Project boundaries

`Maf.Lab.Domain` holds contracts only. `Maf.Lab.Retrieval` owns Qdrant, BM25, embeddings and the MCP server.
`Maf.Lab.Indexing` references Retrieval. `Maf.Lab.Api` references Retrieval + Indexing **for admin endpoints and dev
auth only**; the chat path retrieves exclusively through `search_documents` over MCP (the IL scan shows the API assembly
makes no Qdrant calls). Hosts load `retrieval.json` / `appsettings.json` / `indexing.json` / `eval.json` respectively so
referenced web projects' config files never collide.

## 12. Load balancer (add-load-balancer, 2026-09-19)

- **Single entry point:** nginx `lb` on host port **7171** routes `/api/*` and `/dev/*` to the api pool, `/mcp` to the
  mcp-retrieval pool, `/lb-health` locally and everything else to the static web container. Host ports 5080, 5090 and
  5174 are no longer published; Qdrant (6333/6334) and Ollama (11435) stay published for the host-side CLIs.
- **Why nginx:** one proxy technology in the stack (the web image already pins `nginx:1.30.5-alpine`). Traefik
  (label discovery) and HAProxy (active health checks) were the alternatives.
- **Replica discovery (revised in add-makefile):** upstreams are resolved when nginx (re)loads — Docker DNS returns
  every replica — and `make up` reloads the balancer after scaling or recreation. The original dynamic
  `server … resolve` + `resolver valid=10s` was dropped: `make verify` showed roughly half the runs losing one request
  (504, no retry) when a DNS refresh swapped the peer list while a request was waiting on the stopped replica. With
  static peers the failover check passed on every run. Trade-off: scaling without `make up` needs
  `docker compose exec lb nginx -s reload`.
- **Balancing:** `least_conn` for api (long-lived SSE turns would skew round robin), round robin for MCP. Passive
  health `max_fails=1 fail_timeout=10s` (a dead replica leaves rotation after its first failure), and
  `proxy_connect_timeout 2s`. A stopped container's IP silently drops
  packets, so the default 60 s connect timeout made failover hang; 2 s turns it into a quick retry on another replica.
- **Retries:** `proxy_next_upstream error timeout http_502 http_503`, and nginx does not replay non-idempotent requests,
  so a chat POST is never run twice.
- **SSE and MCP:** `/api/chat` and `/mcp` are relayed with `proxy_buffering off`, `gzip off` and long read timeouts
  (verified: events arrive incrementally through the balancer). MCP 2026-07-28 is stateless, so there is no affinity;
  the agent host itself calls `http://lb/mcp`, so tool calls are balanced too.
- **Replica identity:** `X-Instance: <hostname>` on every response and `instance` in `/health`.
- **Shared state:** admin jobs moved from process memory to the `AdminJobs` table. A partial unique index
  (`FirmId, Kind` where `State = 'running'`) makes "one running job per firm and kind" hold across replicas. The owner
  heart-beats every 10 s; a heartbeat older than 60 s means the owner died, so the job is reported failed
  ("interrupted") and no longer blocks a new start. `GET /api/admin/jobs/{id}` is now scoped to the admin's firm.
- **SQLite with two replicas:** every connection sets `journal_mode=WAL` and `busy_timeout=5000`. Schema creation runs
  the model's create script with `IF NOT EXISTS`, which is race-safe and also adds new tables to an existing database
  (`EnsureCreated` would not). **Trigger to move to Postgres** (allowed by project.md): any `SQLITE_BUSY` reaching users,
  more than one Docker host, or more than a handful of api replicas.
- `compose/pull-models.sh` skips models already present and retries pulls: a transient Docker DNS failure for
  `registry.ollama.ai` had blocked the whole stack.
- Verification: `scripts/verify_lb.sh` (17 checks against the running stack).

## 13. Make (add-makefile, 2026-09-19)

- `make` is the entry point for everything (`make help`). Plain `make` = start: build, run, wait until healthy, reload
  the balancer, index if empty, print the URL. Re-running it is a no-op apart from cache checks (~4 s).
- **GNU Make 3.81 compatible** (macOS ships it): no `.ONESHELL`, `.SHELLFLAGS`, `$(file)` or grouped targets. Multi-line
  logic lives in `scripts/wait_healthy.sh`, `index_if_empty.sh`, `dev.sh` and `doctor.sh`, which are also bash 3.2 safe
  (empty arrays are guarded under `set -u`).
- Tools are discovered with fallbacks: `dotnet` from PATH or `~/.dotnet`, which sets `DOTNET_ROOT`. Host CLIs use the
  compose infrastructure (Qdrant 6333/6334, Ollama 11435).
- `OLLAMA_API_KEY` is only read from the environment: `make up` warns when it is missing, and `make doctor` prints only
  whether it is set or missing, never the value.
- `make dev` stops whole process trees on exit: `dotnet run` and `npm` spawn children, so killing the direct child left
  servers listening. Process substitution keeps `$!` pointing at the server, not at the log prefixer.
- `make clean` is the only destructive target and asks for confirmation (`FORCE=1` skips it). `make down` keeps volumes.

## 14. Continuous integration (add-github-ci, 2026-09-19)

- **Pins:** runner `ubuntu-24.04`, not `-latest`. `actions/checkout@v7` (7.0.1), `actions/setup-dotnet@v6` (6.0.0,
  SDK from `global.json`), `actions/setup-node@v7` (7.0.0, Node 24), `actions/cache@v6` (6.1.0),
  `actions/upload-artifact@v7` (7.0.1). OpenSpec CLI `@fission-ai/openspec@1.13.1` via `make specs`. Workflows are
  linted with actionlint 1.7.12.
- **NuGet cache:** through `actions/cache` on `NUGET_PACKAGES=$GITHUB_WORKSPACE/.nuget/packages`, keyed on
  `Directory.Packages.props` and `*.csproj`, because `setup-dotnet`'s built-in cache requires `packages.lock.json`
  files, which this repository does not use.
- **Model-free e2e:** `compose/ollama-stub` (Python stdlib on `python:3.13.7-alpine`) implements only what OllamaSharp
  calls: `/api/version`, `/api/tags`, `/api/show`, `/api/embed` (feature hashing at 768/384 dimensions, so the Qdrant
  schema is unchanged) and `/api/chat` (NDJSON streamed in chunks of 4 words, 50 ms apart, quoting the first
  `<tool_data>` source). `compose/docker-compose.ci.yml` replaces `ollama` with it, turns `ollama-init` into a no-op and
  points chat at it. Indexing the full corpus takes about 15 s instead of about 11 min. Quality with real models is the
  job of the evals workflow.
- **Secrets:** `OLLAMA_API_KEY` exists only as a repository secret, set through stdin. Only `evals.yml`
  (workflow_dispatch) reads it. Workflows have `permissions: contents: read`. Pull-request runs from forks get no
  secrets, and none of the push/PR jobs needs one.
- **Public repository:** the corpus, seed data and datasets are synthetic, and databases and caches are gitignored.
  Commit authorship becomes public.

## 15. Behind-the-scenes monitor (add-behind-the-scenes-monitor, 2026-09-19)

- **Trace model:** one ordered list of `TraceEvent { seq, atMs, kind, title, durationMs?, data, truncated }` per turn,
  collected in-process by `TurnTrace`. It is streamed as SSE `trace` and stored in `TurnTraces` before `done`, so the
  stored copy is readable as soon as the stream ends. Kinds and data shapes are in `docs/trace-events.md`.
- **Why not OpenTelemetry as the source:** the GenAI spans from M.E.AI and the Agent Framework carry flattened string
  attributes, and they do not see MCP or retrieval internals. Ordering and live streaming would need our own collector
  anyway. The kinds follow GenAI naming, so an exporter can be added later.
- **Capture points:**
  - the runner: turn, intent, prompt, sources, signals and end;
  - `SqliteChatHistoryProvider`: the history window and memory writes;
  - `TracingChatClient`: every real model call. It sits below `RequiredToolModeChatClient`, which reports calls it
    issued on the model's behalf as `tool.forced`;
  - the tool middleware: call, raw result, retrieval, envelope and audit (with `callId`).
- **Retrieval internals via MCP `_meta`:**
  - The agent sends `_meta {"maf-lab/trace": true}` through `McpClientTool.WithMeta`.
  - `search_documents` then adds `_meta["maf-lab/trace"]` with tenant scope, settings, BM25 terms and IDF, the dense,
    sparse and fused candidates, rerank order and timings, plus `_meta["maf-lab/instance"]`.
  - The per-branch lists come from extra dense-only and sparse-only queries through `TenantScopedSearch`, so they are
    tenant-filtered like everything else. `Retrieval:TraceBranches` turns them off.
  - The envelope for the model is built from structured content only, and the diagnostics are removed from `tool.result`
    (tested).
- **Access:** the owner, or a FIRM_ADMIN of the same firm for turns in the review queue; otherwise 404. Trace content
  never goes to logs (tested with markers).
- **Retention and caps:** `TraceRetentionService` deletes traces older than `Tracing:RetentionDays` (7) every hour and is
  replica-safe. Caps: 20,000 characters per field and 1 MB per trace, with `truncated` marked.
- **SSE payload:** the `trace` event's data is the `TraceEvent` itself, not the internal `TraceChatEvent` wrapper. A
  test that asserted `seq` caught the wrapped form.
- **UI:** a two-pane grid that stacks under 1024 px. Retrieval candidates show the file and last section levels, with the
  full chunk id in the tooltip; long ids had wrapped character by character in the narrow columns.

## 16. Trace time travel (add-trace-time-travel, 2026-09-19)

- **Answer in the trace:** the streamed answer is recorded as `answer.delta { offset, text }` events. A chunk is
  flushed at 160 characters, after 150 ms, before any tool call and at the end of the turn (also on errors). The
  offsets are contiguous and the chunks concatenate to exactly what the client received (tested). A 220-word answer
  gives a handful of events instead of ~220 deltas. The SSE `text_delta` stream is unchanged, so the trace carries
  the text only to make the chat reconstructable.
- **Time-travel model:** it is client-side and pure. Every view is a function of `(events, cursor)`. The cursor is
  either a step number or "live" (following the head); any manual move stops following until "Back to live".
  Playback schedules the next step after the recorded `atMs` gap divided by the speed, with gaps capped at 1 s when
  "compress waits" is on (the default), because a single model call can take several seconds.
- **Chat reconstruction:** `reconstructTurn(events, cursor)` rebuilds the answer text from `answer.delta`, the tool
  cards from `tool.call`/`tool.result`, and the sources from the `sources` event. Traces stored before this change
  have no `answer.delta`: they fall back to the final text and say so.
- **Chunk ordering:** `TracingChatClient` flushes the answer buffer just before recording `model.response`. Streams
  are pull-based, so every delta has already reached the runner at that point. The first live run showed the last
  chunk being recorded after `model.response` and `memory`, because it was flushed only at turn end.
- **Out of scope:** time travel across turns or conversations, and server-side re-execution of a turn.

## 17. Chat history (add-chat-history, 2026-09-19)

- **Additive schema evolution:** `DatabaseInitializer` now runs in three steps: create missing tables, add missing
  columns (`PRAGMA table_info` then `ALTER TABLE … ADD COLUMN`, with NOT NULL columns getting typed defaults), and
  finally create indexes, because indexes may reference the new columns.
  - Two replicas starting together are fine: a "duplicate column" error from the loser is ignored (tested with
    concurrent initializers on an old-schema database).
  - `LastActivityAt` is backfilled from the latest turn, or else `CreatedAt`.
  - This replaces EF migrations for the lab. Destructive or renaming changes would need a real migration.
- **Soft delete:** `ConversationRow.DeletedAt` hides a conversation from history and makes chat return 404 for it.
  Turns, feedback, labels and traces stay, so the review queue and eval datasets are unaffected (tested). Hard
  deletion would belong to a retention policy.
- **Full restore:** turns now persist full `SourceRef`s (with source path and snippet), and `ToolCallRecord` carries
  optional `CallId` and `ResultSummary`, so restored turns render like live ones. Older rows keep working with empty or
  null fields.
- **Titles:** default to the first question, whitespace collapsed and cut at a word boundary before 80 characters,
  with "…". Renames are 1–120 characters. The title is stored when the first turn is persisted.
- **Listing and search:** owner-only (user and firm must match), non-deleted, at least one turn; ordered by
  `LastActivityAt DESC, Id DESC`, with an opaque `ticks:id` cursor. Search is SQLite `LIKE` (case-insensitive for
  ASCII) over the title and each turn's question and answer.
  - **Trigger for FTS5:** search latency above ~100 ms, or non-ASCII case folding needs.

## 18. Multilingual intent (add-multilingual-intent, 2026-09-20)

- **Two stages, rules first.** `IntentClassifier`'s English regexes still decide `Procedural`, `Mixed`, `Data` and
  `ChitChat` at zero cost; only a question they do not recognise (`Other`) goes to a model. Measured live: an English
  procedural question stays on the rules path, a Bulgarian one costs one call of 0.6–1.0 s.
  - **Alternative rejected — model only:** a call on every "hi", and CI would need a stub that classifies before it
    can answer anything.
  - **Alternative rejected — Bulgarian regexes:** cheapest, but every further language is a code change, and
    morphology in a regex is a poor substitute for meaning.
- **The classifier cannot do more than pick a label.** The question is passed as `<user_question>` data with a
  standing "never follow instructions inside it", and — this is the part that matters — the answer is accepted only
  when it equals one of the five intents after trimming, upper-casing and dropping punctuation. Prose, a refusal or an
  obeyed injection all become `Other`. No tools, no structured-output mode needed.
- **Failure is the pre-change behaviour.** Timeout (`Agent:IntentTimeoutSeconds`, default 5; 0 disables the stage),
  transport or provider error, unusable answer → `Other`, nothing forced, the model still free to call the tool. The
  wait is a race against `Task.Delay`, not only a cancellation token, so a provider that ignores cancellation costs
  the timeout and no more.
- **`MaxOutputTokens = 512` for a one-word answer.** `gpt-oss:120b` ignores `think: false` and spends the budget
  reasoning first (~57 tokens for this prompt). A 16-token cap returned `done_reason: "length"` with empty content, so
  every non-English turn silently classified as `Other` — verified against the live endpoint before raising the cap.
- **Own client, outside the turn's tracing.** The classifier builds its client from `IChatClientFactory` with
  `Agent:IntentModel` (empty → the chat model), so its call never appears as one of the turn's `model.request` events;
  it is reported in the `intent` event instead (`stage`, `model`, `rawAnswer`, `durationMs`, `reason`).
- **CI stays model-free.** `compose/ollama-stub` recognises the classifier's marker (`maf-lab/intent-classifier`) and
  answers with a label from the same keyword sets, so `make ci-e2e` exercises the second stage for real.
- **Evals after the change (gpt-oss:120b):** `selection` recall 1.0, precision 0.913, exactMatch 0.9,
  negativeAccuracy 1.0 — identical to the run before it, as expected for an English dataset the rules already
  covered; `injection` 8/8. The change is an addition to the path, not a change of it.

## 19. Topology view (add-topology-view, 2026-09-20)

- **The drawing is parsed, not exported.** `docs/topology.drawio` is authored as *uncompressed* mxGraph XML and the
  web app reads its geometry, labels and edges to render its own SVG. So a person decides the layout in draw.io while
  the state comes from the system, with no export step to forget. draw.io's compressed save (one base64 blob) is
  rejected by the parser with that message, and by a test.
  - **Alternative rejected — export an SVG:** no place to overlay per-node state, and the export rots silently.
  - **Alternative rejected — generate the diagram:** always correct, never legible.
  - **Alternative rejected — embed the draw.io viewer:** a heavy third-party script for a page that must work in a lab.
- **The diagram and the report cannot drift.** A test compares the drawn vertex ids with `TopologyProbe.NodeIds` in
  both directions and names what is missing (verified by deleting a node: "reported but not drawn: [web]").
- **Replicas are discovered by DNS, then asked.** `Dns.GetHostAddressesAsync("api")` returns one address per replica —
  the same fact nginx relies on — and each is asked the anonymous `/health` that both hosts already serve, which
  answers with its own instance name. No new table, no heartbeat timer, no Docker socket. A *stopped* container leaves
  DNS, so it cannot be listed: the node then shows `discovered: 1 address(es)` rather than a dead replica. A replica
  that is up but silent *is* listed, as `unreachable`, and degrades its service.
  - Outside the container network (`make dev`, tests) nothing resolves; the report says so and speaks for this
    instance only, instead of failing.
- **What is deliberately not probed:** the chat provider. It is a paid remote endpoint (Ollama Cloud), and pinging it
  every 5 s to learn what configuration already says is waste — it is reported `NotProbed` with the model, the
  endpoint and *whether* `OLLAMA_API_KEY` is set. The key itself never leaves the process (asserted by a test).
- **Probing cannot become load.** Every probe runs concurrently with a 2 s budget, one failure never fails the report,
  and the whole report is cached for 5 s across requests and replicas.
- **MCP health is two questions.** Each replica's `/health`, plus one `tools/list` through the balancer. A failed
  `tools/list` while a replica is up is `Degraded` (the path is broken), not `Unreachable` — found while testing with
  one replica stopped, where the first answer was wrongly "unreachable" with a healthy replica listed.
- **`NodeHealth` is serialized by name.** The default enum-as-number would have reached the web app as `0`, which its
  types do not accept; a test now asserts `"health":"Healthy"` on the wire.

## 20. Multilingual retrieval (add-multilingual-retrieval, 2026-09-20)

- **Measured before deciding.** Against the running stack, a Bulgarian question and its English twin shared *zero*
  of the top-5 documents in three of four probes; the fourth shared 3/5 only because `FS-REQUIRED` is Latin text
  BM25 could match. Over the eval set, hybrid recall@5 was **0.208 for Bulgarian** against 0.693 for English.
- **Translate the query, do not swap the embedding model.** A multilingual dense model (bge-m3, multilingual-e5)
  would fix the dense half without a per-query model call, and `dense_v2` is provisioned for exactly that
  experiment. It was rejected as the primary fix because BM25 would still tokenise Cyrillic terms that appear in no
  chunk, so hybrid search would collapse to dense-only for precisely the questions that need help. Translation
  fixes both halves. The experiment stays open: the eval now reports recall per language, so pointing `dense_v2` at
  a multilingual model is a measurement rather than an argument.
- **In the retrieval server, not in the agent.** `DocumentSearchService.RankAsync` is the one place every caller
  passes through — the agent's forced call, a model-chosen call, the eval harness, a raw MCP client. A system-prompt
  line ("search in English") would have been free but would only cover the agent, would leave the eval measuring
  something else, and would be invisible in the trace.
- **Only a query that needs it.** The check is the *script*, not the language: a query whose letters are all Latin
  is searched as written, so every English path costs exactly what it did before. Its only failure mode is a missed
  translation for a Latin-script non-English query, never a wrong one. Translations are cached per replica.
- **Failure is the old behaviour.** Timeout (`Retrieval:TranslationTimeoutSeconds`, 5 s), provider error, or an
  answer that is empty, multi-line, far too long or still not in the corpus language → the original query is
  searched, and the reason appears in the diagnostics. `Retrieval:NormalizeQueryLanguage=false` disables it.
- **`InvariantGlobalization=true`** is on for these hosts, so `CultureInfo.GetCultureInfo("en").EnglishName` returns
  `en`, not `English`. The prompt takes the language name from a small explicit table instead — found by a test.
- **Results (gpt-oss:120b, hybrid):** recall@5 for Bulgarian **0.208 → 0.681**, English **0.693 → 0.693**
  (unchanged, which was the acceptance criterion), overall 0.456 → 0.687, recall@20 0.612 → 0.922, MRR 0.402 →
  0.635. `selection` (recall 1.0, precision 0.913) and `generation` (faithfulness 0.969, relevance 1.0) unchanged.

## 21. Compliance audit (add-compliance-audit, 2026-09-20)

- **One record, not two.** Tool calls, deletions and exports share `Audit`, separated by `Kind`. A second table
  would need its own chain, and two chains cannot be ordered against each other — while the whole value is one
  sequence in which a deletion sits between the tool calls before and after it.
- **The chain follows row ids, never clocks.** `Hash = sha256(previousHash ⋮ stored fields)`. Two replicas share one
  SQLite file and their clocks can disagree; row ids cannot. The head is read and the row written inside one
  serializable transaction, so two replicas appending at the same moment cannot link to the same predecessor
  (tested with twelve parallel appends).
- **The digest must survive a round trip.** The first version hashed `At` with `"O"`, which renders a `Utc` kind as
  `…Z` and the `Unspecified` kind the store returns without it — every verification failed. The canonical form now
  forces UTC. Found by a test, not in production.
- **The 65 pre-existing rows are not rewritten.** They verify as "before the chain". Back-filling hashes would have
  been the one thing an audit trail must never do: restate the past.
- **Deletion is recorded, a refused deletion is not.** Someone else's conversation still returns 404 and is
  attributed to nobody, because nothing was deleted. Stated in the spec so the absence is deliberate.
- **The export's digest is canonical, not JSON.** The first version hashed `JsonSerializer` output, so only this
  service could reproduce it — a digest nobody else can recompute is not worth publishing. Caught while re-hashing a
  live export in Python. The canonical rendering then failed again because .NET writes 100-nanosecond ticks that
  Python's microsecond timestamps truncate; timestamps are now millisecond precision, and the Python re-hash
  matches. The format is documented in `docs/http-api.md`.
- **The export records itself before returning**, so a failed download still leaves the attempt recorded; its
  manifest carries the chain head from *before* its own row, since a package cannot contain its own digest.
- **`(PrincipalId, At)` index** beside `(FirmId, At)`: an investigation starts from a person.
- **Not solved, and said so:** tamper-evidence is not immutability (needs append-only external storage), and the dev
  token issuer means identity is not provable. Both are in the README rather than implied away.

## 22. Eval regression gate (add-eval-regression-gate, 2026-09-20)

- **A committed baseline, not the last report.** `evals/reports/` is gitignored, so a CI run has no history; and
  comparing with the newest local report *ratchets downwards* — every run measures against a slightly worse
  predecessor, so a slow slide never trips anything. `evals/baseline.json` states what "good" currently is where a
  diff can show it moving.
- **Accepting is a separate act.** `make eval-accept` runs the suites and writes the baseline; a plain run never
  touches the file, or the first re-run after a regression would launder it. A run below its thresholds is refused,
  since accepting it would bless exactly what the floors rejected. The file records the run id it came from.
- **`new` and `missing` are signals, not noise.** A metric the baseline does not mention, and a baseline metric the
  run did not produce, are both reported: together they are what a rename looks like, and a renamed metric is how a
  gate quietly stops gating.
- **The tolerance is per suite, because the noise is.** Measured over four runs with everything else unchanged:
  `recall@5:en` was 0.693 every time, while `recall@5:bg` alternated between 0.660 and 0.681 — a 0.021 swing,
  because a non-English query is translated by a live model and a different translation retrieves different chunks.
  The default 0.02 sat exactly on that boundary, the worst possible place. `retrieval` now uses 0.03; the others
  keep 0.02. The number comes from measurement, not from raising it until the gate went green.
- **Floating point needed slack.** A drop *exactly* at the tolerance must pass, and `1.0 - 0.98` is
  `0.020000000000000018`; the comparison carries 1e-9 of slack. Found by a test.
- **The comparison travels in the report**, so `/evals` shows what moved without recomputing it and an old report
  explains itself. Reports written before the gate carry none and still load.
- **The trend is drawn from local reports, the gate never is.** The screen plots a metric across whatever runs this
  machine has, with the baseline marked — 14 runs at the time of writing, showing `recall@5:bg` climbing from 0.188
  to 0.646 over one day.

## 23. A2A hosting (add-a2a-hosting, 2026-09-20)

- **Which packages, and why they are not interchangeable.** `A2A` and `A2A.AspNetCore` 1.0.0-preview2 carry the
  protocol itself. The Agent Framework's own A2A packages (`Microsoft.Agents.AI.A2A`,
  `Microsoft.Agents.AI.Hosting.A2A[.AspNetCore]`, 1.22.0-preview.260918.1) are a layer *on top of those exact
  versions*, not an alternative to them: their nuspecs depend on `A2A 1.0.0-preview2`. So the wire behaviour below
  is the same whichever one is referenced.
- **The client package is used; the hosting package is not.** `Microsoft.Agents.AI.A2A` turns a remote agent into
  an `AIAgent` (`A2ACardResolver.GetAIAgentAsync`), which is how the verification client — and, next, the
  compliance sub-agent — talks to an A2A agent. The hosting bridge would have been the natural way to expose ours,
  but `A2AAgentHandler` is `internal` in this preview and reachable only through `MapA2AJsonRpc(agent, …)`, which
  takes an `AIAgent` rather than an `IAgentHandler`. An `AIAgent` cannot express `rejected` or `input-required`,
  and both are required here — a partner asking about another firm's run, and a run whose period is missing. The
  handler is therefore ours, and the assistant's own answers are produced by running the same `ChatClientAgent`
  the chat UI runs, under a token minted for the partner's firm.
- **Both transports are mapped, gRPC is not.** `MapA2A` (JSON-RPC) and `MapHttpA2A` (HTTP+JSON) both answer behind
  the partner policy. The SDK ships no gRPC server, so the card does not advertise one.

### Where the preview SDK departs from A2A 1.0

Verified by driving the endpoint, not by reading release notes. `SpecWire` translates each one in both
directions, and every item disappears from the code the day the SDK speaks 1.0 itself:

| The specification | 1.0.0-preview2 |
| --- | --- |
| `message/send`, `tasks/get`, `tasks/pushNotificationConfig/set`, … | `SendMessage`, `GetTask`, `CreateTaskPushNotificationConfig`, … |
| `"role": "user"` / `"agent"` | `ROLE_USER` / `ROLE_AGENT` |
| `"state": "input-required"` | `TASK_STATE_INPUT_REQUIRED` |
| a part carries `"kind"`, a file part nests `file.bytes`/`file.uri` | no `kind`; `raw`/`url`/`mediaType`/`filename` flattened onto the part |
| a message, task or artifact update carries `"kind"` | no `kind` |
| a status update carries `"final"` | absent; the caller is left to work out when the stream ends |
| the result *is* the task or message | wrapped: `{"task": …}`, `{"message": …}`, `{"statusUpdate": …}` |
| `pushNotificationConfig`, server-assigned id | `config` plus a **required** `configId` the caller must invent |

- **Five methods throw.** `A2AServer` implements send, stream, get, list, cancel and subscribe, but
  `Create/Get/List/DeleteTaskPushNotificationConfigAsync` and `GetExtendedAgentCardAsync` throw
  `NotImplementedException` — while `IA2ARequestHandler` declares them and our card advertises them. They are
  implemented in `A2ARequestHandlerWithExtras`, which delegates everything else untouched.
- **`ITaskStore` had to be ours.** The SDK ships only `InMemoryTaskStore`, which two replicas behind the balancer
  cannot share: a task started on one would not exist on the other. `SqliteTaskStore` keeps it in the database the
  replicas already share, and — being the one place every transition passes through — is also where push delivery
  is triggered, outside the write transaction so a slow webhook never holds it.
- **The card signature is symmetric (HS256) because the lab's issuer is.** A real deployment would sign with a key
  whose public half is published, so a partner can verify without holding a secret. Said plainly rather than
  implied.
- **Push is at-least-once with a shared token.** One POST per transition, retried `A2A:PushRetries` times, the
  caller's own token echoed in `X-A2A-Notification-Token`, and a failure recorded rather than raised: a receiver
  being down is not the task's problem. A real deployment would sign the notification instead.
- **A partner registration grants only what it names.** `PartnerRegistration.Scopes` starts empty, because
  configuration binding *appends* to a non-empty default — a partner configured with only the write scope would
  have silently kept the read one too.
- **Options are read per request, not at start-up.** The partner policy resolved `A2AOptions` once while the
  pipeline was built, which in a test host reads configuration that has not been added yet: every partner token
  was authenticated and then forbidden. It now reads `IOptionsMonitor` when a request is authorized.
- **The answer follows the dialect the question was asked in.** Serving 1.0 and serving the SDK's spelling are
  mutually exclusive on one endpoint: a specification client sends `message/send` and cannot read
  `{"task": {…}}`, while the Agent Framework's own A2A client sends `SendMessage` and cannot read
  `{"kind": "task"}`. The middleware therefore answers in whichever dialect the request used — translated when the
  caller spoke 1.0, untouched when it spoke the SDK's. `scripts/a2a_probe.py` proves the first, the
  `Maf.Lab.A2AProbe` tool proves the second, and both run against the balancer.
- **Resubscription follows the store, not the SDK's event channel.** The channel lives in one process; the
  balancer sends a reconnecting caller to whichever replica is free, which is usually the other one. Live, that
  looked like a stream delivering the task and then hanging until the caller timed out. `SubscribeToTaskAsync` is
  therefore ours: the current task first, then every change read from the shared store until the task is done.
- **A run outlives the connection that asked for it.** The handler took the request's cancellation token, so a
  dropped stream killed the run mid-way and the task stayed `working` for ever — the opposite of what
  resubscription is for. The simulated run now stops only for an explicit cancel or the host shutting down.
- **The card is served with the protocol's serializer.** The signature covers a canonical rendering — the card
  without `signatures`, members sorted, no whitespace — and `Results.Ok` was serializing the served document with
  different options, so a partner recomputing the digest got a different answer. Found by verifying the live card
  from Python, which is now part of `scripts/a2a_probe.py`.
- **A push delivery carries a Content-Length.** Chunked delivery is legal and broke a receiver that reads by
  length — including the probe's. The payload is serialized before the request is built.

## 24. A second agent, consulted (add-compliance-agent, 2026-09-20)

- **Three projects came out of one.** `Maf.Lab.A2A` holds what both agents need — partner identity, the card
  factory, the 1.0 wire translation, the request handler — and `Maf.Lab.Hosting` holds the thirty lines every
  service shares (`X-Instance`, `/health`). Putting the A2A code in `Maf.Lab.Retrieval` would have made the MCP
  server and the indexer carry the protocol packages; copying it into the reviewer would have let the two agents
  drift apart on the wire. `AuthOptions` moved to `Maf.Lab.Domain` for the same reason: everything that mints or
  validates a token needs it, including services that know nothing about retrieval.
- **The card factory takes a descriptor.** Name, description, skills and scopes come from the service; the two
  transports, the security scheme, the signature and the canonical rendering are shared, so the two cards cannot
  disagree about the parts that are not about the agent. A recorded copy of the billing card, captured from the
  running stack before the change, is asserted byte for byte after it.
- **The required scope is configuration.** The policy used to demand `a2a.billing.read` by name, which is
  meaningless at an agent that reviews adjustments. `A2A:RequiredScope` names it per service; empty means any
  scope the partner's registration grants.
- **A consultation returns a result, it does not throw.** `ConsultationResult` is a verdict, a question, a
  timeout, an unreachable agent or a failure. The caller — the workflow, in the next change — must handle each,
  and a type that enumerates them is better than a `catch` that hopes. A timeout keeps the task id on purpose: the
  review is still running over there.
- **The reviewer keeps its tasks in memory, and the balancer keeps a task on its replica.** It has no other state
  and nothing outside it reads a finished review, so a database would be ceremony. The cost is that a replica's
  in-flight reviews die with it; `hash $request_uri consistent` keeps a task's follow-ups on the replica that owns
  it, and the caller's deadline is what turns a lost review into a truthful answer rather than a hang.
- **`A2ACardResolver` appends the well-known path to the origin, not to the address it is given.** With two agents
  behind one entry point, asking `http://lb/compliance` for a card returns the *billing* agent's. Found live, by
  the probe, after the unit tests passed — they served the reviewer at a root. The card is now requested by full
  path, and a test serves the reviewer under a prefix so the bug cannot come back.
- **The assistant holds its own credentials at the reviewer**, and the two agents' tokens are not
  interchangeable: different audiences, and `scripts/verify_lb.sh` asserts that a billing token is refused at
  `/compliance/a2a`.

## 25. The first write (add-fee-adjustment, 2026-09-20)

- **`Microsoft.Data.Sqlite` 10.0.12 is pinned** and referenced by `Maf.Lab.Retrieval`. The ledger is one table;
  EF Core there would mean a second `DbContext`, a second model and a second migration story for nine columns.
- **The MCP server got its own store.** Applied adjustments live in `Maf.Lab.Retrieval`'s SQLite on the
  `retrieval-data` volume, not in the API's database. The alternatives were worse: having the MCP tool call back
  into the API makes the server the host calls start calling the host, with a second authentication hop for every
  write; moving the write tool into the API breaks the project's own rule that a write is confirmed at the MCP
  layer and gives the model tools from two different sources. The cost is a second database, and it is named
  here so nobody discovers it by accident.
- **The seed stays read-only; the ledger is the only thing that moves.** An account's current fee is its seeded
  fee plus every adjustment applied to it, so `compose/seed/billing-accounts.json` remains a fixture and a test
  can reason about it.
- **MRTR `input_required`, not elicitation.** Both exist in ModelContextProtocol 2.2.0. Elicitation would park
  the tool call inside one API replica while the only channel to the user is that replica's SSE response, and
  the browser has no way to answer mid-stream; recovering a lost connection would need a pending-call registry
  pinned to a replica. MRTR ends the turn, and the answer can arrive minutes later, on any replica.
  `DECISIONS.md` §7 recorded MRTR as "not exercised here"; it is exercised now.
- **The client resolves input requests itself.** `McpClient.CallToolAsync` sees an `InputRequiredResult`, calls
  the registered `ElicitationHandler` and retries — with no handler it throws "no ElicitationHandler is
  registered". There is no opt-out. So the API registers a handler that does not decide: it takes the question
  down and answers `cancel`, which the tool treats as *nobody was asked* rather than as a refusal. A dismissal
  and a refusal must not be the same thing, or a user who closes a tab has declined an adjustment.
- **The proposal is signed, not stored.** The state carries the adjustment id, firm, user, account, amount, a
  digest of the reason and an expiry, under an HMAC. The second call executes the state and **ignores the
  arguments**, because the arguments pass through a language model. Nothing needs storing until something is
  applied, and a confirmation can land on any replica. The key is `Billing:ProposalSigningKey`, falling back to
  `Auth:SigningKey`; symmetric, because the issuer and the verifier are the same service — a real deployment
  would sign asymmetrically so a verifier need not hold the secret. The reason travels as a digest so no free
  text rides along.
- **The database enforces "applied once".** `UNIQUE (FirmId, AdjustmentId)` refuses the second insert, and that
  refusal is the answer "already applied" rather than an error. The same `AdminJobRow` pattern: let the store
  hold the invariant instead of a check that races two replicas.
- **A pending proposal is a row in the API's database** (`PendingAdjustments`), because the person who answers
  may reach a different replica or come back tomorrow. It holds the state, the summary shown, the review's task
  id and how many times the reviewer has asked — never the advisor's words.
- **No `Microsoft.Agents.AI.Workflows`.** The flow is one branch (a threshold), one bounded loop (at most two
  questions) and one wait. Taking the package would add a dependency and a second place where control flow
  lives to express a dozen lines of C# as a graph. What would change it: a second sub-agent, a fan-out, or a
  flow that must survive a process restart half-way.
- **Why one assistant agent.** The triggers for splitting an agent are different system prompts, different
  permissions, different owners or different context budgets. None applies: the billing assistant has one
  prompt, one principal and one budget, and the reviewer — which does have its own prompt, owner and identity —
  is already a separate service reached over A2A. A second *local* agent would be ceremony.
- **No OpenTelemetry.** The Day-4 brief asked for spans carrying the A2A task id. The repo has no OTel and no
  `ActivitySource`; it has its own turn trace, which the monitor renders and time travel replays. Each step of a
  write is an `adjustment` trace event carrying the adjustment id and, where one exists, the task id.
  Introducing a whole observability stack to satisfy one sentence would have been the wrong trade; the gap is
  recorded instead of half-built.
- **A verdict is checked before it is believed.** It must carry a decision and name the same adjustment *and*
  the same account that was sent; anything else counts as a failed review, not as an approval or a refusal. The
  identifiers carried onwards are the ones this system sent — previously the consumer took `adjustmentId` from
  the reply, which made an untrusted system the source of the correlation key. The reviewer now echoes the
  account id so there is something to compare.
- **A consultation streams.** `SendMessageAsync` cannot report a task id when the deadline fires, so a timeout
  returned an empty id — exactly when `a2a-client` requires the id be kept. `SendStreamingMessageAsync` gives the
  id in the first event, and what is known when the deadline falls is all there is.
- **The card says where it answers; configuration says how to get there.** Found live: the assistant's
  consultations came back `unreachable` although the reviewer was up. A card is public, so the reviewer
  advertises `http://localhost:7171/compliance/a2a` — which, from inside the compose network, is the api
  container itself. Discovery worked (it uses `Compliance:BaseUrl`), the send did not. The consultant now takes
  the **path** from the card's `supportedInterfaces` and the **origin** from its configured address, as any
  client behind a reverse proxy does. Assembling the path here instead would be hard-coding a route, which is
  what the card exists to avoid. This was a pre-existing bug from add-compliance-agent: nothing called the
  consultant in a production path until this change, and `scripts/verify_lb.sh` reaches the reviewer from the
  host, where `localhost:7171` *is* the balancer.
- **A type union change can break a build that lint and tests do not run.** Adding `confirmation_required` to
  the web's `ChatStreamEvent` made `chatReducer`'s exhaustive switch non-exhaustive. `make lint` (eslint +
  prettier) and `make test-web` (vitest) both passed; only `tsc -b`, which runs in `make build-web` and in the
  web image build, caught it — and the failed image build left the stack running the *previous* images, which
  is how a "healthy" stack served three tools instead of four. `make ci` runs `build-web`, so CI would have
  caught it; worth remembering when verifying by hand.
- **Defaults.** `FeeAdjustments:ReviewAboveAmount` is 500 (absolute), `FeeAdjustments:MaxQuestions` is 2,
  `Billing:ProposalValidFor` is 30 minutes.

## 26. One protocol for the wire (add-agui-stream, 2026-09-20)

- **`AGUI.Server` and `AGUI.Abstractions` 1.0.0 are pinned.** Published by the AG-UI Protocol organisation
  (github.com/ag-ui-protocol/ag-ui), MIT, stable since 2026-09-17. The server package describes itself as a
  framework-agnostic adapter from `Microsoft.Extensions.AI` chat streams to AG-UI events, which is this
  project's chat loop named. They depend on `Microsoft.Extensions.AI.Abstractions` 10.6.0; NuGet resolves
  upward to the pinned 10.10.0, which `dotnet list package --include-transitive` confirms.
- **The adapter maps the model's output, inside the existing pipeline.** Three options: hand-write the protocol
  (rejected — the fiddly parts are message ids, ordering and when a text message opens and closes, which is what
  the SDK exists to get right); restructure the turn around the adapter and move audit, tracing and the
  fee-adjustment flow behind its mapping hooks (rejected — the trace begins before the model is called and the
  flow can block for a minute mid-call; neither is a mapping of a content item); or let the adapter own the
  model's output while the runner keeps the channel, the order and everything else. The third. `ChatTurnRunner`
  enumerates `AsChatResponseUpdatesAsync(agent stream).AsAGUIEventStreamAsync(context, ct)` and writes what
  comes out into the same channel its own events go into.
- **What the adapter produces is not what a client may see.** It attaches the whole originating
  `ChatResponseUpdate` to every event as `rawEvent` — carrying a tool call's arguments and a tool result's full
  payload, document text included — and renders arguments and results verbatim. Its mapping hooks add events
  *after* the built-in ones rather than replacing them, so they cannot prevent it. Every event therefore passes
  through `RunRedaction`: `rawEvent` is dropped, `TOOL_CALL_ARGS` becomes the identifier-only summary the audit
  already computes, and `TOOL_CALL_RESULT` becomes `{ tool, summary, sourceCount, isError }`. Found by a probe
  before any of it shipped.
- **The runner owns the run's beginning and end.** The adapter emits its own `RUN_STARTED`/`RUN_FINISHED`, but a
  turn's first trace events happen before the model is ever called, and its last word may be that it is waiting
  for a person. Those are dropped and the runner emits its own.
- **A failed run must be read off the stream.** The adapter catches what the model throws and turns it into a
  `RUN_ERROR` rather than letting it out, so without watching for that event a failed turn would have ended as a
  success. A test caught it. A run that was *stopped* is not a failure, so cancellation is checked first.
- **Confirmation-before-write is an interrupt, not an invention.** The Day-4 brief planned a custom
  `confirmation_required` event. The protocol already has `AGUIInterrupt` — id, message, response schema, tool
  call id, expiry, metadata — and `RunFinishedInterruptOutcome`, answered by an `AGUIResume` on the run that
  continues. Every field the proposal needed had a slot. The proposal's expiry, which until now only the signer
  knew, is carried to the host through the elicitation's `_meta` so it can reach `expiresAt`.
- **`POST /api/chat/confirm` was deleted, not kept as an alias.** It was three hours old with no consumers, and
  two ways to answer one proposal is one too many.
- **Sources and the trace are custom events**, `maf-lab/sources` and `maf-lab/trace`. The protocol says a
  consumer may ignore a custom event it does not know, which is what makes them safe to add.
- **The web client translates; it does not adopt an SDK.** `toChatEvents` maps protocol frames to the reducer's
  existing actions, so `chatReducer`, the monitor and time travel kept their shape and their tests. The
  protocol's TypeScript packages would be a second dependency for a client that renders six kinds of event.
- **A stop only reaches the replica running the turn.** Runs are registered per instance; the balancer spreads
  requests, so a stop sent elsewhere answers 404 rather than pretending. What a browser actually does — abandon
  the stream — always works, because the run's token hangs off the request's own. The in-memory test host does
  not abort a request the way a real socket does, so that half is a live check and a unit test of the wiring.

## 27. A write a person can see (add-confirmation-ui, 2026-09-20)

- **What survives a closed tab is the proposal, not the run.** The Day-4 brief asked the client to re-attach to a
  run in progress and receive a state snapshot. That would mean storing runs, which nothing else needs, to solve
  a problem the server already solved: the proposal is a durable row, and approving twice applies once. Opening a
  conversation asks `GET /api/conversations/{id}/pending` and renders the same card. A stream that was abandoned
  is over, and saying so is more honest than pretending to resume it.
- **The row learned two things it did not keep.** The sentence a person was asked and the expiry were computed
  when the proposal was made — by the tool and by the signer — and thrown away. Both are stored now, because a
  card rebuilt after a reload has to ask the same question with the same deadline.
- **The expiry is decided twice, on purpose.** The card stops offering the buttons once it has passed, because
  asking someone to press a button that cannot work is worse than telling them. The server refuses an expired
  state regardless, and if the clocks disagree the server's refusal is what the card then shows.
- **Three faces, decided where the failure is known.** `useChatStream` knows whether it saw a status code, a
  `RUN_ERROR` or a dropped connection, so it dispatches a kind — `unavailable`, `refused`, `unexpected` — and the
  page renders the kind. A refusal says only that the conversation is not available: why is the server's
  business, and explaining it would be explaining someone else's data. The rule that nothing internal is rendered
  is asserted against the DOM, not against the strings in the source.
- **The fourth feedback kind is a stored enum with three owners** — the domain's allowlist, the web's union and
  the button list — so a round-trip test posts it, reads it back from the review queue and proves the three
  agree. The button appears only on a turn that asked for a confirmation, because a turn without one has no
  summary to be wrong. It deliberately does *not* add a labelling dataset to the review queue: the fourth kind
  is judged by its own suite, not by hand.
- **The confirmation suite has no judge model.** A summary either states the account, the amount and the
  resulting fee the server computed, or it does not — an arithmetic question, not a matter of taste. It is
  checked against the sentence a person actually reads, formatted the way they read it, so a change in how the
  number is written is a change the suite notices.

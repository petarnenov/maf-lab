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

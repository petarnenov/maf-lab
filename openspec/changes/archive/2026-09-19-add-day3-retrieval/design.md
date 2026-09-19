# Design

## Context

Greenfield repository: only OpenSpec scaffolding exists. Motivation is in
proposal.md; behavior is in `specs/`. The stack, repository layout, and hard
conventions (tenant from principal only, one query builder, DTO tool results,
no message content in logs) are fixed by `openspec/project.md`. The dev machine
currently has Node 24 and Docker but no .NET SDK, so the first task installs
the .NET LTS SDK and pins versions in `DECISIONS.md`.

## Goals / Non-Goals

**Goals:**
- A readable reference architecture: each spec requirement maps to one obvious
  place in code.
- Every tenant-isolation guarantee is enforced structurally (types and a single
  choke point), then proven by tests — not by reviewer discipline.
- Every quality claim (chunking, fusion, rerank, contextual retrieval, prompt)
  is measurable by the eval harness with a config switch.

**Non-Goals:**
- Horizontal scaling, HA, or production observability backends.
- Real identity provider; the dev issuer is the only token source.
- Pixel-level UI design.

## Decisions

### D1. Project boundaries
`Maf.Lab.Domain` holds only contracts (`Principal`, `TenantId`, `Role`, result
DTOs, SSE event records). `Maf.Lab.Retrieval` owns Qdrant access, BM25, the
MCP server and the stub billing tools. `Maf.Lab.Indexing` references
`Maf.Lab.Retrieval` for the store writer and BM25 encoder so the vocabulary and
payload schema have one owner. `Maf.Lab.Api` references Retrieval + Indexing
only for the admin endpoints (index, drift, migrate) and dev auth; the chat path
retrieves exclusively through `search_documents` over MCP, and the API assembly
makes no Qdrant calls (enforced by the IL scan in D2).
*Alternative*: agent host queries Qdrant directly for chat — rejected; it would
create a second query path and blur the tool boundary the spec requires.

### D2. Single tenant choke point
`TenantScopedSearch.QueryAsync(Principal, SearchRequest)` in
`Maf.Lab.Retrieval` is the only method that calls `QdrantClient.QueryAsync`.
It builds a `should(tenant_id == principal.FirmId, tenant_id == "shared")`
filter and attaches it to *each* prefetch branch and to the outer query.
`SearchRequest` has no tenant field. The enumeration test uses reflection +
Roslyn-free IL scan (Mono.Cecil) over all `Maf.Lab.*` assemblies to find every
call site of Qdrant data-plane methods and asserts the only callers are
`TenantScopedSearch` (query), `TenantScopedMaintenance` (writes and admin reads,
each scoped to one tenant from the corpus layout or a FIRM_ADMIN principal) and
`Bm25Store` (meta collection only).
*Alternative*: Qdrant JWT RBAC per tenant — deferred; documented in
DECISIONS.md as a defense-in-depth follow-up.

### D3. Qdrant collection layout
One collection `maf_chunks`; named vectors `dense_v1` (dim of the current
embedding model, cosine), later `dense_v2`, and sparse `bm25` with
no Qdrant IDF modifier (IDF computed in-repo). `dense_v2` is provisioned at
creation because Qdrant cannot add a named vector to an existing collection.
`tenant_id` keyword index
with `is_tenant=true`; HNSW `m=0`, `payload_m=16` so graphs are built per
tenant. Keyword indexes on `source_type`, `doc_id`, `model_version`; datetime
index on `updated_at`. Point id = deterministic GUID from SHA-256(chunk_id)
so re-upserts overwrite. Tiered multitenancy trigger (documented in
DECISIONS.md): any tenant above ~20% of points or a p95 latency SLO breach →
promote to a dedicated shard.

### D4. Hybrid query
Query API with two prefetches (dense, sparse), each `limit = max(5 × limit,
50)` and the tenant filter, fused with `Fusion.Rrf` (config `Fusion=Dbsf`
switches). Modes `Hybrid|Dense|Sparse` select branches. Rerank is an
`IReranker` with `NoOpReranker` default and an Ollama-backed implementation;
failure falls back to no-op with a structured warning.

### D5. BM25 encoder
In-repo tokenizer (lowercase, Unicode letter/digit split, English stopwords,
light stemming off by default), vocabulary as term → int id, IDF = BM25 idf
over the indexed corpus, k1=1.2, b=0.75; document side stores TF-saturated
weights, query side sends idf weights. Vocabulary + stats persisted as JSON
in a `maf_meta` Qdrant collection (single point) so both indexer and MCP
server load the same version. *Alternative*: Qdrant built-in `bm25`/SPLADE
via FastEmbed — rejected by project.md (no external service, learning goal).

### D6. Indexing pipeline
`ISourceLoader` per folder convention `data/{tenant}/{docs|procedures|code}/`;
tenant = first path segment (`firm-a`, `firm-b`, `firm-c`, `shared`); any
other location → rejected. Chunkers: Markdig AST for headings; regex on
numbered steps / `Section` headers for procedures; a lightweight
brace/indent-aware splitter for C#/TS/Python (tree-sitter avoided to keep
dependencies small). doc_id = `tenant/relative-path` and chunk_id =
`doc_id#section-slug[-n]` (stable and readable in eval datasets). Re-index =
upsert new points, then delete the doc's other points (no window where the
document is missing; same one-version outcome). Contextual enrichment calls the chat model with the whole document
(truncated) + chunk, cached by chunk hash. Drift compares filesystem mtime
with the max `updated_at` per doc_id.

### D7. Embedding migration
`migrate --to <vector>`: `dense_v2` already exists (provisioned at creation);
scroll in batches with filter `model_version != v2` and
use Qdrant `UpdateVectors` with a conditional filter so already-migrated points
are skipped; `model_version` payload set in the same batch. Restart simply
re-runs the filtered scroll. `Retrieval:DenseVector` config selects which
named vector queries use. Where the .NET client lacks conditional updates, the
gap is recorded in DECISIONS.md and emulated by the filtered scroll.

### D8. MCP server
ModelContextProtocol C# SDK with ASP.NET Core Streamable HTTP transport in
stateless mode. JWT bearer auth middleware produces the `Principal`, available
to tool handlers through DI (`IPrincipalAccessor`). Tools declared with
`[McpServerTool]` returning `CallToolResult` with structured content and
output schemas; annotations set explicitly. Billing stubs read
`compose/seed/billing-runs.json` into a record type that has a `Note` field,
mapped to a DTO that does not.

### D9. Agent host
Microsoft Agent Framework `ChatClientAgent` over an `IChatClient` from
Microsoft.Extensions.AI (Ollama provider; OpenAI/Azure by config). MCP tools
are loaded via the framework's MCP client integration, forwarding the user's
bearer token. Intent classifier: rules first (how/why/procedure/explain/what
is patterns, no run id) → sets `ToolMode.RequireSpecific("search_documents")`
for that run only. Function-invocation middleware writes the audit log and
wraps results in `<tool_data source="...">…</tool_data>` with the data notice;
unknown tool names are intercepted and returned as an error. SSE via
`TypedResults.ServerSentEvents` mapping agent update stream → domain events.
Memory: SQLite (EF Core) tables `conversations(id, user_id, firm_id)` and
`messages`, trimmed by a token-count window (tokenizer estimate) before each
call. Feedback and signals stored in the same SQLite DB.

### D10. Web
Vite + React + TS. `useChatStream` hook reads SSE via `fetch` +
`ReadableStream` (EventSource cannot send auth headers) and dispatches into a
pure `chatReducer`; `feedbackReducer` handles optimistic feedback state; both
unit-tested with Vitest. TanStack Query for reports, drift, feedback queue.
Dev token picker (firm/role) calls the dev issuer.

### D11. Eval harness
Console app with `--suite selection|retrieval|generation|injection|all`
(`--rerank`, `--contextual`, `--limit N`, `--import-feedback`). Selection,
generation and injection run the production turn path (`ChatTurnRunner` →
MCP client → retrieval MCP server hosted in-process on loopback unless
`Evals:McpEndpoint` is set) and read the tool calls from the turn record, so
the deployed tool descriptions and intent forcing are what gets measured.
Retrieval ranks through `DocumentSearchService.RankAsync` (same tenant-scoped
query path, k=20) with an eval principal and always reports hybrid, hybrid-dbsf,
dense and sparse variants; thresholds gate the configured hybrid variant. Judge uses a fixed rubric prompt with the configured chat model.
Reports to `evals/reports/<timestamp>-<suite>.json|.md`; the API serves the
directory read-only for `/evals`. Feedback import reads labeled rows from the
SQLite DB and appends to JSONL with a `source: feedback` marker.

## Risks / Trade-offs

- [Local Ollama chat models are weak at tool selection] → evals quantify it;
  provider is switchable to OpenAI/Azure by config for comparison.
- [MCP SDK may lag spec 2026-07-28 (stateless core)] → implement over the
  SDK's transport where needed; record every gap in DECISIONS.md.
- [Agent Framework MCP integration may not forward per-user bearer tokens] →
  custom `HttpClient` handler per request scope; recorded if needed.
- [LLM-as-judge noise] → fixed rubric, temperature 0, report variance over
  repeated runs.
- [`m=0` HNSW means no global graph; cross-tenant admin search is slow] →
  acceptable; there is no cross-tenant query path by design.
- [Heuristic code chunker misses edge cases] → fall back to fixed-size windows
  with overlap and flag in logs.

## Migration Plan

Greenfield; no deployment migration. Rollback of the embedding migration is
switching `Retrieval:DenseVector` back to `dense_v1`.

## Open Questions

- Exact Ollama chat model (e.g. qwen3 vs llama3.x) — chosen by the selection
  eval; does not change specs or tasks.

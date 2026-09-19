# Proposal

## Why

The team's assistant will expose RAG as a tool from an MCP server over a
Qdrant store shared by many firms. maf-lab needs a complete, end-to-end
reference implementation of that shape — multi-tenant retrieval exposed as a
tool, consumed by a Microsoft Agent Framework agent, evaluated and defended
against prompt injection — so every design decision can be read, run and
measured before it lands in the production system.

## What Changes

- Establish tenancy: firms are tenants; a Principal (user id, firm id, role,
  allowed advisor ids) is derived only from a JWT minted by a local dev
  issuer. The tenant filter is mandatory on every retrieval and provably
  applied on every query path.
- Add an indexing pipeline for three source types (Markdown docs, procedures,
  code) with type-specific chunkers, one chunk metadata schema, optional
  contextual-retrieval enrichment, dense + BM25 sparse vectors, idempotent
  re-indexing by doc_id, drift detection, and a restartable embedding-model
  migration.
- Configure one Qdrant collection with payload-based multitenancy (per-tenant
  HNSW sub-graphs), payload indexes, and hybrid search (dense + sparse
  prefetch, server-side RRF/DBSF fusion, optional switchable rerank).
- Add an MCP server (spec 2026-07-28, Streamable HTTP, stateless) exposing
  `search_documents` plus two read-only stub data tools
  (`get_billing_run_status`, `search_billing_runs`) so tool selection is a
  real problem.
- Add an agent host (ASP.NET Core) consuming those tools through MCP, with
  a few-shot system prompt, conditional forced retrieval for procedural
  questions, audit middleware, SSE streaming of text and tool-call events,
  and token-windowed conversation memory persisted outside the process.
- Add prompt-injection defenses: delimited data blocks for tool output, no
  side-effecting tools, audit of calls to non-existent tools, and an
  injection corpus with a tested eval.
- Add a React web app: `/chat` (streaming, tool-call cards, sources,
  structured feedback), `/evals`, `/admin/index`, `/admin/feedback` (review
  queue fed by production signals, labeling into eval datasets).
- Add an evaluation harness (CLI) with JSONL datasets and metrics for tool
  selection, retrieval, generation, and injection; reports consumed by the
  web app; thresholds in configuration.
- Add Docker Compose (qdrant, ollama, api, mcp-retrieval, web), a sample
  corpus for three firms (one 10x larger) plus shared content, VS Code
  launch/tasks, and `DECISIONS.md`.

## Capabilities

### New Capabilities
- `tenant-isolation`: principal derivation from the dev-issuer JWT, roles, and the guarantee that every retrieval is scoped to the caller's firm plus shared content.
- `document-indexing`: loading and chunking the three source types, chunk metadata, contextual enrichment, dense/sparse encoding, re-indexing, drift reporting, and embedding-model migration.
- `hybrid-retrieval`: vector-store layout and query behavior — hybrid dense+sparse search with fusion, selectable modes, optional rerank, and per-tenant recall guarantees.
- `retrieval-tool`: the MCP server contract — `search_documents` and the two stub billing tools, their schemas, descriptions, annotations, and error behavior.
- `chat-agent`: the agent's tool use, conditional forced retrieval, audit logging, SSE event stream, and persisted conversation memory.
- `injection-defense`: treatment of retrieved and tool content as data, handling of non-existent tool calls, and the tested guarantee that embedded instructions are not followed and no cross-tenant data leaks.
- `web-ui`: the chat, evals, index-admin and feedback-review screens, including structured feedback capture.
- `eval-harness`: eval datasets, metrics, retrieval modes, reports, thresholds, and import of labeled feedback.

### Modified Capabilities
<!-- None: greenfield project with no existing specs. -->

## Impact

- New code: `src/Maf.Lab.Api`, `src/Maf.Lab.Retrieval`, `src/Maf.Lab.Indexing`,
  `src/Maf.Lab.Eval`, `src/Maf.Lab.Domain`, `web/`, `compose/`, `data/`,
  `evals/`, `.vscode/`, `DECISIONS.md`.
- New dependencies: .NET LTS SDK, Microsoft Agent Framework 1.x,
  ModelContextProtocol C# SDK, Qdrant.Client, Microsoft.Extensions.AI,
  xUnit + Testcontainers, React/Vite/React Router/TanStack Query/Vitest.
- New runtime services: Qdrant, Ollama (embedding + chat model), SQLite or
  Postgres for conversations and feedback.
- Out of scope (follow-up changes): write tools and MRTR confirmation, A2A /
  multi-agent / AG-UI, OAuth discovery, tiered multitenancy with dedicated
  shards, production-grade reranker.

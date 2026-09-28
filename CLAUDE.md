# maf-lab

TAMP billing RAG assistant. Read openspec/project.md first — it holds the
stack, layout, and hard conventions. Active change proposals live under
openspec/changes/.

Non-negotiables while editing:
- Tenant (firm_id) comes from the principal only. Never add a tenant
  parameter to a tool, an endpoint, or a query builder.
- One method builds Qdrant queries and applies the tenant filter. Do not
  add another.
- Tool results are DTOs designed for the model; never return entities.
- No message content in logs.
- If a package version must move, update DECISIONS.md in the same commit.

Chat model: `gpt-oss:120b` on Ollama Cloud — needs `OLLAMA_API_KEY` in the environment.
Intent classifier: TypeSafe Jev (`jev-1.13.0`), the only one; the same key routes data turns (api) and judges search
relevance (mcp-retrieval) — needs `JEV_MAF_LAB` in the environment,
sent only as the bearer header; never put it in a prompt, state, trace or log.
Embeddings: local Ollama, one multilingual model (`embeddinggemma`, vector `dense_v3`). Changing it = new profile + `make rebuild-index FORCE=1`.

Entry point: everything runs behind the nginx load balancer on http://localhost:7171 (api and mcp-retrieval x2).

Commands (see `make help`):
- `make` — start everything on http://localhost:7171 (build, wait healthy, index if empty)
- `make down` · `make ps` · `make logs SERVICE=api`
- `make test` · `make lint` · `make verify` · `make eval SUITE=selection`
- `make index` · `make drift` · `make dev` (local, no Docker for app services) · `make doctor`

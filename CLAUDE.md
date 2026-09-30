# maf-lab

TAMP billing RAG assistant. Read openspec/project.md first — it holds the
stack, layout, and hard conventions. Active change proposals live under
openspec/changes/.

Non-negotiables while editing:
- TOP PRIORITY: every CLI tool shows a progress bar for the work it does, and
  every UI-started process that can take longer than 3 seconds shows progress
  in the page's theme and design (spec: `progress-feedback`).
- Tenant (firm_id) comes from the principal only. Never add a tenant
  parameter to a tool, an endpoint, or a query builder.
- One method builds Qdrant queries and applies the tenant filter. Do not
  add another.
- Tool results are DTOs designed for the model; never return entities.
- No message content in logs.
- If a package version must move, update DECISIONS.md in the same commit.
- Never edit inside a `generated:` block; edit its source and run `make docs`.
  A change that alters a route, target, project, model or lb location
  updates its docs in the same change; `make docs-check` (in CI) enforces it.

Chat model: `gpt-oss:120b` on Ollama Cloud — needs `OLLAMA_API_KEY` in the environment.
Intent classifier: TypeSafe Jev (`jev-1.13.0`), the only one; the same key routes data turns (api) and judges search
relevance (mcp-retrieval) — needs `JEV_MAF_LAB` in the environment,
sent only as the bearer header; never put it in a prompt, state, trace or log.
Embeddings: local Ollama, one multilingual model (`embeddinggemma`, vector `dense_v3`). Changing it = new profile + `make rebuild-index FORCE=1`.

Entry point: everything runs behind the nginx load balancer on http://localhost:7171 (api, mcp-retrieval, mcp-portfolio and compliance x2; mcp-code x1).

Commands (see `make help`):
- `make` — start everything on http://localhost:7171 (build, wait healthy, index if empty)
- `make down` · `make ps` · `make logs SERVICE=api`
- `make test` · `make lint` · `make verify` · `make eval SUITE=selection`
- `make index` · `make drift` · `make dev` (local, no Docker for app services) · `make doctor`
- `make docs` (rewrite generated doc blocks) · `make docs-check` (docs vs code; also in `make ci`)

- Before touching any TypeSafe Jev call, read docs/rules/jev-usage.md.

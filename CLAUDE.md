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

Commands:
- `docker compose -f compose/docker-compose.yml up -d`
- `dotnet run --project src/Maf.Lab.Indexing`
- `dotnet run --project src/Maf.Lab.Eval -- --suite all`
- `cd web && npm run dev` / `npm test`

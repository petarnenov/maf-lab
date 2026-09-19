# Proposal

## Why

The stack currently exposes each service on its own host port (web 5174, api 5080, mcp-retrieval 5090) and runs a
single instance of everything. A production assistant sits behind one entry point and scales its stateless tiers
horizontally; the lab should exercise that shape — including SSE streaming and stateless MCP through a balancer, and
the places where per-process state breaks once there is more than one replica.

## What Changes

- Add an nginx **load balancer** service as the single entry point on host port **7171**, routing `/` to web,
  `/api/*` and `/dev/*` to the api pool, and `/mcp` to the mcp-retrieval pool.
- Run **2 replicas** of `api` and of `mcp-retrieval`; the balancer distributes requests across them and stops
  sending to a replica that fails.
- The agent host reaches the MCP server **through the balancer**, so tool calls are balanced too.
- **BREAKING (dev ergonomics):** direct host ports 5080, 5090 and 5174 are removed from compose. Qdrant (6333/6334) and
  Ollama (11435) stay published for the host-side indexing and eval CLIs.
- Admin jobs (index, migrate) move from in-process memory to the shared database so their status is visible from any
  api replica; conversations, feedback and audit already live in the shared database and must keep working across
  replicas.
- Each replica identifies itself (response header and health payload) so balancing is observable and testable.
- The web container becomes static-only; routing is the balancer's job.

## Capabilities

### New Capabilities
- `load-balancing`: single entry point on port 7171, path routing to the service pools, balancing across replicas,
  SSE and stateless-MCP behaviour through the balancer, health, and cross-replica consistency of admin jobs and
  conversations.

### Modified Capabilities
<!-- None: existing requirements (chat SSE events, MCP contract, admin screens) keep their behaviour; the new
     capability adds the constraints that make them hold behind the balancer. -->

## Impact

- `compose/docker-compose.yml` (new `lb` service, replicas, port changes, internal MCP endpoint), new
  `compose/lb/nginx.conf`, `web/nginx.conf` (static only), `src/Maf.Lab.Api` (DB-backed admin jobs, instance header,
  SQLite WAL/busy timeout), `src/Maf.Lab.Retrieval` (instance header), tests, README, DECISIONS.md.
- No new NuGet/npm packages; one new image: nginx (same pinned version as the web image).

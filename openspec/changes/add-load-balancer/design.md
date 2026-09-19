# Design

## Context

See proposal.md for motivation. Today (after add-day3-retrieval) compose publishes web on 5174 (its own nginx proxies
`/api` and `/dev` to a single `api`), api on 5080 and mcp-retrieval on 5090. The api keeps conversations, turns,
feedback, labels and audit in SQLite on the `api-data` volume, but admin jobs live in an in-memory
`AdminJobRunner`, which breaks as soon as a status poll lands on another replica. The MCP server is already stateless
(protocol 2026-07-28), so it balances without affinity. Chat is SSE over `POST /api/chat`.

## Goals / Non-Goals

**Goals:**
- One entry point on 7171; 2 replicas each of api and mcp-retrieval; all specified behaviour unchanged through it.
- Make every cross-request state replica-safe, and prove balancing is real (observable instance ids).

**Non-Goals:**
- TLS termination, auth at the edge, rate limiting, autoscaling, multi-host deployment.
- Scaling Qdrant or Ollama (infrastructure stays single-instance and host-published).
- Replacing SQLite with Postgres (see Risks for the trigger).

## Decisions

### D1. nginx as the balancer
Use `nginx:1.30.5-alpine` (the image already pinned for web) with a dedicated `compose/lb/nginx.conf`.
Upstreams `api_pool`, `mcp_pool`, `web_pool` use `server <service>:<port> resolve` with a shared `zone` and
`resolver 127.0.0.11 valid=10s`, so Docker DNS returns every replica and the list refreshes when containers restart
(the `resolve` parameter is available in open-source nginx since 1.27.3). Balancing method: `least_conn` for api
(long-lived SSE connections would skew round robin), round robin for mcp. Passive health: `max_fails=2
fail_timeout=10s`; `proxy_next_upstream error timeout http_502 http_503` for idempotent requests only
(`non_idempotent` not set, so a failed chat POST is not replayed).
*Alternatives:* Traefik (label-driven discovery, heavier, new tool to learn) and HAProxy (active checks, another
config language). nginx keeps one proxy technology in the stack.

### D2. Replicas
`deploy.replicas: 2` for `api` and `mcp-retrieval` (Compose honours it without Swarm); no `container_name` and no
host ports on those services. Scaling further is `docker compose up --scale api=N`.

### D3. Routing and SSE
`/api/chat`: `proxy_buffering off`, `proxy_cache off`, `gzip off`, HTTP/1.1 with `Connection ""`,
`proxy_read_timeout 600s`, `X-Accel-Buffering: no`. `/api/` and `/dev/` → api_pool; `/mcp` → mcp_pool (buffering
off too, since Streamable HTTP may answer as an SSE stream); `/lb-health` served by nginx; everything else → web_pool.
`X-Forwarded-For/Proto/Host` are set. The web container's nginx becomes static-only (SPA fallback), removing the
double proxy hop.

### D4. Agent → MCP through the balancer
`Agent__McpEndpoint=http://lb/mcp` in compose. The per-user bearer token is forwarded as today, so tenant derivation
is unchanged. Each turn creates a new MCP client, and stateless MCP needs no affinity.

### D5. Replica identity
Middleware in both hosts adds `X-Instance: <hostname>` to every response; `/health` returns `{status, instance}`.
Tests and the verification script use it to prove requests spread across replicas.

### D6. Admin jobs in the database
Replace the in-memory dictionary with an `AdminJobs` table (id, firm_id, kind, state, started_at, finished_at,
summary, owner_instance, heartbeat_at). Starting a job inserts a `running` row inside a transaction that first checks
for a live job of the same firm and kind (live = running with a heartbeat younger than 60 s). The owning replica
updates `heartbeat_at` every 10 s and writes the final state. Status reads come from the table, so any replica can
answer. A job whose owner died (stale heartbeat) is reported `failed` with "interrupted" and no longer blocks a new
start. The public `AdminJob` contract is unchanged.

### D7. SQLite shared by two api replicas
Both replicas mount the same `api-data` volume on one Docker host. Enable `journal_mode=WAL` and
`busy_timeout=5000` at connection open (a connection interceptor), so concurrent writers wait instead of failing.
Schema creation (`EnsureCreated`) is idempotent and races are harmless: the loser retries once.
The `api-data` volume is already local to the Docker VM, so file locks work.

### D8. Ports
Remove host ports from api, mcp-retrieval and web; add `lb` with `7171:80`. Keep qdrant 6333/6334 and ollama 11435.
Local, non-Docker development (dotnet run, vite on 5174) is unchanged and documented as the "without balancer" mode.

## Risks / Trade-offs

- [SQLite write contention with two replicas] → WAL + busy timeout; the trigger to move to Postgres is any
  `SQLITE_BUSY` surfacing to users, or more than one Docker host. Postgres is explicitly allowed by project.md.
- [Long SSE streams are cut if a replica restarts] → acceptable; the client shows `done.error` or a network error and
  the user retries. No replay of non-idempotent POSTs.
- [nginx `resolve` needs a zone and a resolver] → configured explicitly; a startup test proves both replicas receive
  traffic.
- [Contextual-retrieval cache file written from two replicas] → only the admin-triggered indexing writes it, and
  indexing is a single DB-guarded job per firm; the last write wins, which is safe for a cache.

## Migration Plan

`docker compose -f compose/docker-compose.yml up -d --build` recreates the stack; the Qdrant index and the api-data
volume are reused (the new `AdminJobs` table is created on startup). Bookmarks move from `:5174` to `:7171`.
Rollback: check out the previous compose file and web nginx.conf; the extra table is ignored by older code.

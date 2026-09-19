# Tasks

## 1. Replica-safe API state

- [x] 1.1 Add an `AdminJobs` table and a DB-backed job runner (insert-if-no-live-job, 10 s heartbeat, stale → failed "interrupted"), keeping the `AdminJob` contract; verify unit tests for start, duplicate start returning the running job, status from a second runner instance, and stale-heartbeat takeover
- [x] 1.2 Enable SQLite `journal_mode=WAL` and `busy_timeout=5000` via a connection interceptor and make schema creation race-tolerant; verify a test with two API hosts on one database file writing turns concurrently without errors
- [x] 1.3 Verify cross-replica conversation continuity: a test with two API hosts sharing one database where turn 2 on host B sees turn 1 from host A

## 2. Replica identity

- [x] 2.1 Add `X-Instance` response middleware and `instance` in `/health` for api and mcp-retrieval; verify tests assert the header and payload

## 3. Load balancer

- [x] 3.1 Add `compose/lb/nginx.conf` (resolver, zone upstreams with `resolve`, least_conn for api, SSE settings, `/mcp`, `/lb-health`, SPA routing to web); verify `nginx -t` passes in the pinned image
- [x] 3.2 Make `web/nginx.conf` static-only (SPA fallback, asset caching, `/healthz`); verify web tests/build still pass and the container serves `index.html` for deep links
- [x] 3.3 Update compose: `lb` on `7171:80`, `deploy.replicas: 2` for api and mcp-retrieval, remove host ports 5080/5090/5174, `Agent__McpEndpoint=http://lb/mcp`, lb depends on healthy api/web; verify `docker compose config` is valid and `up -d` brings all services healthy with 2+2 replicas

## 4. Verification through the balancer

- [x] 4.1 Add `scripts/verify_lb.sh` that checks: 7171 serves `/`, `/admin/feedback` deep link, `/dev/users`; ports 5080/5090/5174 refused; 20 `/api/me` calls show ≥2 `X-Instance` values; MCP tools/list works through `/mcp`; verify the script passes against the running stack
- [x] 4.2 Verify SSE through the balancer: a procedural question on 7171 yields `tool_call_started`, `tool_call_finished`, `sources`, `done` in order and events arrive incrementally (timestamps of first and last event differ); include in the script
- [x] 4.3 Verify replica failure: stop one api replica, confirm requests through 7171 still succeed, restart it; include in the script
- [x] 4.4 Verify admin jobs through the balancer: start indexing on 7171, poll status repeatedly (served by different instances) until `succeeded`, and a second start while running returns the same job id; include in the script
- [x] 4.5 Run the full .NET and web test suites and `--suite selection` eval against the stack's MCP (`Evals__McpEndpoint=http://localhost:7171/mcp`); verify all pass

## 5. Documentation

- [x] 5.1 Update README (entry point 7171, no-balancer local dev mode, scaling with `--scale`), `docs/http-api.md` base URL, and DECISIONS.md (balancer choice, nginx `resolve`, SQLite WAL decision and Postgres trigger, removed ports); verify sections exist

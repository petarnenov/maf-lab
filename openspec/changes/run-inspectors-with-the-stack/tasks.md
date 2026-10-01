# Tasks

No Jev call is added or changed, so the Jev review checklist does not apply.

## 1. Compose and the balancer

- [x] 1.1 Add `listen 7171;` to the server in `compose/lb/nginx.conf`; verify `make up` reloads it and `curl localhost:7171/lb-health` still answers
- [x] 1.2 Publish `127.0.0.1:7172:7172` and `127.0.0.1:7173:7173` on `lb`; verify with `docker port` that both are bound to 127.0.0.1 only
- [x] 1.3 Add the `a2a-inspector` service (profile `inspectors`, `network_mode: service:lb`, build from the pinned git commit, uvicorn on 7172, Python urllib healthcheck, `depends_on: lb healthy`); verify it becomes healthy and `http://localhost:7172` returns 200
- [x] 1.4 Add the `mcp-inspector` service (profile `inspectors`, `network_mode: service:lb`, `ghcr.io/modelcontextprotocol/inspector:2.9.0`, `CLIENT_PORT=7173`, no volume, `depends_on: lb healthy`); verify it becomes healthy and `http://localhost:7173` returns 200
- [x] 1.5 Add the `redis-insight` service (profile `inspectors`, `redis/redisinsight:3.8.0`, `127.0.0.1:7174:7174`, `RI_APP_PORT=7174`, `RI_REDIS_HOST=redis`, `RI_REDIS_PORT=6379`, `RI_REDIS_ALIAS=maf-lab`, `RI_ACCEPT_TERMS_AND_CONDITIONS=true`, `RI_FILES_LOGGER=false`, Node `fetch` healthcheck on its health endpoint, no volume, `depends_on: redis healthy`); confirm the health path inside the container, then verify it becomes healthy and `http://localhost:7174` returns 200
- [x] 1.6 Remove the hand-started `a2a-inspector` container from earlier (host network, port 7172) before the first `make up`, so the port is free

## 2. Make and scripts

- [x] 2.1 Export `COMPOSE_PROFILES := inspectors` unless `CI_MODE=1`; verify `make ps` lists the three inspectors and `make -n up CI_MODE=1` runs without the profile
- [x] 2.2 Banner lists `A2A Inspector → http://localhost:7172` `MCP Inspector → http://localhost:7173` and `Redis Insight → http://localhost:7174`; verify with `make banner`
- [x] 2.3 `scripts/dev.sh` stops `a2a-inspector` and `mcp-inspector` (they live in `lb`'s namespace) with the other app containers; verify by reading the script and `bash -n`
- [x] 2.4 Verify `make down` leaves no inspector container running

## 3. Ready-to-use defaults

- [x] 3a.1 `compose/a2a-inspector/lab_app.py` wraps upstream's app (card URL filled, Bearer selected, fresh partner token from `/lab/token`, token follows the agent when the URL changes); verified in the browser: page opens filled, Connect loads the card, switching to the compliance card yields a token with audience `maf-lab-compliance`
- [x] 3a.2 `compose/mcp-inspector/start.mjs` writes the catalog (billing, portfolio, code; dev token; hourly refresh) and starts the Inspector; verified: the three servers are listed with a token, and switching "maf-lab billing" on connects (MCP 2026-07-28)

## 3b. Links in the web UI

- [x] 3.1 Add "A2A Inspector", "MCP Inspector" and "Redis Insight" after "Curriculum" in `web/src/components/Layout.tsx` (host from `window.location`, `target="_blank"`, `rel="noopener noreferrer"`, `↗`, accessible name with "opens in a new tab"), styled like the nav links; verify in the browser in both themes
- [x] 3.2 Vitest test for the three links (order after Curriculum, hrefs from the page host, target/rel, accessible names); verify `make test-web` and `make lint-web` pass

## 4. End-to-end checks

- [x] 4.1 From inside each inspector's namespace, `http://localhost:7171/.well-known/agent-card.json` and `http://localhost:7171/mcp` answer (e.g. `docker compose exec a2a-inspector python -c ...`)
- [x] 4.2 A2A: through the A2A Inspector's backend, connect to `http://localhost:7171` with a token from `/a2a/token` and get an answer to a message (manual, in the browser)
- [x] 4.3 MCP: connect the MCP Inspector to `http://localhost:7171/mcp` with a `/dev/token` bearer and list tools (manual, in the browser)
- [x] 4.4 Redis: the Redis Insight API lists the pre-configured `maf-lab` database with no pending licence, and its browser shows the lab's keys with TTLs (manual, in the browser); host port 6379 stays closed
- [x] 4.5 `make verify` still passes (the application's single entry point and routing are unchanged); passed twice in a row after the two fixes below
- [x] 4.6 `scripts/verify_lb.sh` reloads the balancer after restarting the replica it stopped (a restarted replica can come back on a new address, and the balancer resolves upstreams only on reload); verified: two consecutive `make verify` runs pass
- [x] 4.7 Dev data: account A-1042's fee had been driven below zero by adjustments on 2026-09-29 (−4116, −2744, before the below-zero guard), so verify could never undo its own +200; restored to the seeded 1200 with one compensating +3650 adjustment through `propose_fee_adjustment` (ledger kept; nothing deleted)

## 5. Documentation

- [x] 5.1 README: "Inspecting A2A, MCP and Redis" section (URLs, addresses to enter, how to get each bearer token, loopback-only note, Redis Insight can write to Redis, macOS note)
- [x] 5.2 CLAUDE.md entry-point line names the inspectors on 7172/7173/7174
- [x] 5.3 `openspec/project.md` Containers list gains `a2a-inspector`, `mcp-inspector` and `redis-insight`; `docs/http-api.md` A2A and MCP sections point to the inspectors; `docs/shared-state.md` points to Redis Insight
- [x] 5.4 DECISIONS.md entry with the pinned A2A Inspector commit, the MCP Inspector and Redis Insight tags, and Redis Insight's SSPL/EULA note
- [x] 5.5 Run `make docs` and `make docs-check`; verify docs-check passes

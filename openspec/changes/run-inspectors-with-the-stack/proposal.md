# Proposal

## Why

The lab exposes two protocol surfaces to other systems — A2A (`/a2a`, `/compliance/a2a`) and MCP (`/mcp`,
`/portfolio/mcp`, `/code/mcp`) — but has no interactive way to look at them from the outside: send a message, see the
card, list tools, call one, read the raw JSON-RPC. The official inspectors for both protocols exist, yet running them
by hand is fiddly in exactly this setup: the agent card advertises `http://localhost:7171/a2a`, and an inspector in a
container resolves `localhost` to itself, so a plain `docker run` reaches nothing unless it uses host networking, which
macOS does not give by default. The same holds for the lab's shared state in Redis (run state, A2A tasks, push
configs, test-generation checkpoints and leases): it is only reachable inside the compose network, so looking at a key,
its TTL or a live `MONITOR` means `docker compose exec redis redis-cli`. The inspectors should simply be there whenever
the lab is.

## What Changes

- Three new compose services that start with the rest of the stack on `make` / `make up`:
  - `a2a-inspector` — the official A2A Inspector (`a2aproject/a2a-inspector`), built from a pinned commit, on
    `http://localhost:7172`.
  - `mcp-inspector` — the official MCP Inspector (`ghcr.io/modelcontextprotocol/inspector`, pinned tag), on
    `http://localhost:7173`.
  - `redis-insight` — Redis Insight (`redis/redisinsight`, pinned tag), Redis's official UI, on
    `http://localhost:7174`, already connected to the lab's `redis` (keys with TTL and formatted JSON values, hashes,
    sorted sets, CLI/Workbench, Profiler/`MONITOR`), with its licence prompt accepted and analytics off by configuration.
- The A2A and MCP inspectors share the load balancer's network namespace, and the balancer also listens on 7171 inside it, so the URLs a
  developer uses on the host (`http://localhost:7171/...`, including the ones the agent cards advertise) work unchanged
  from inside the inspectors — on macOS (Docker Desktop) and Linux alike, with no host networking. Redis Insight needs
  no such trick: it reaches `redis:6379` by service name on the compose network.
- They open ready to use, with nothing typed by hand: the A2A Inspector with the assistant's card URL and a fresh
  partner token (switching the URL to the compliance card switches the token), the MCP Inspector with the lab's three
  MCP servers listed, each carrying a dev user's bearer token, and Redis Insight connected to the lab's Redis.
- All three ports are published on `127.0.0.1` only: the MCP Inspector's backend spawns processes on request and the
  page embeds its API token, and Redis Insight has no login of its own and can write to Redis, so none of them may be on
  the local network.
- They belong to a compose profile, `inspectors`, which `make` enables by default and `CI_MODE=1` leaves off, so CI
  and the model-free end-to-end run do not build or pull them. `make down`, `ps`, `logs` and `restart` cover them.
- `make` prints the three inspector URLs in its banner, and the web UI's main navigation links to them after
  "Curriculum" (new tab, same host as the page).

Progress feedback: they start inside `make up`, whose existing wait shows compose's per-service progress and the
indeterminate health wait; the first build of the A2A Inspector image and the first image pulls show compose's own progress. No new CLI.

## Capabilities

### New Capabilities

- `protocol-inspectors`: the A2A, MCP and Redis inspectors that run with the stack — where they are, what they can reach,
  who can reach them, and when they are left out.

### Modified Capabilities

- `web-ui`: a new requirement — the main navigation links to the three inspectors.

`load-balancing`'s "Single entry point on port 7171" forbids publishing the api, mcp-retrieval and web services
on another port; the inspectors are separate developer tools and the application stays on 7171 alone, so it still
holds. The topology report and diagram describe the services the lab is made of; the inspectors are not part of it and
are not added there.

## Impact

- `compose/docker-compose.yml` — three services in profile `inspectors`: two with `network_mode: service:lb` (the `lb`
  service publishes `127.0.0.1:7172` and `127.0.0.1:7173`), and `redis-insight` publishing `127.0.0.1:7174` itself.
- `compose/lb/nginx.conf` — the server also listens on 7171 (inside the container only; the host mapping stays
  `7171:80`).
- `Makefile` — `COMPOSE_PROFILES=inspectors` exported unless `CI_MODE=1`; the banner lists the inspectors.
- `web/src/components/Layout.tsx` (+ its CSS module and a Vitest test) — the three external links.
- `compose/a2a-inspector/lab_app.py` — wraps the upstream app unchanged: its page plus a defaults script, and
  `/lab/token`, which mints a partner token from the dev credentials compose passes in.
- `compose/mcp-inspector/start.mjs` — writes the Inspector's catalog (three servers, dev token refreshed hourly) and
  starts it.
- `scripts/verify_lb.sh` — reloads the balancer after its replica-failure check restarts a replica, so the checks
  after it see every replica again.
- `scripts/dev.sh` — stops the inspectors with the other containers it stops, since they live in the lb's namespace.
- `DECISIONS.md` — the pinned A2A Inspector commit and the MCP Inspector and Redis Insight tags.
- No .NET or npm package version moves in the repository.

## Documentation impact

- `README.md` — a short "Inspecting A2A, MCP and Redis" section: the three URLs, which address to enter, and how to
  get a bearer token (`/dev/token` for MCP, `/a2a/token` / `/compliance/a2a/token` for A2A).
- `CLAUDE.md` — the entry-point line names the inspectors on 7172/7173/7174.
- `openspec/project.md` — the Containers list gains `a2a-inspector`, `mcp-inspector` and `redis-insight` (then `make docs` for the
  OpenSpec context it generates).
- `docs/http-api.md` — the A2A and MCP sections point to the inspectors for interactive use.
- `docs/shared-state.md` — points to Redis Insight for looking at the keys it describes.

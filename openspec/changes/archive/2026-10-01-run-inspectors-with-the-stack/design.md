# Design

## Context

Everything the lab serves is behind `lb` (nginx, host `7171` → container `80`). The A2A agent cards advertise
`http://localhost:7171/a2a` and `http://localhost:7171/compliance/a2a`, and developers address MCP servers as
`http://localhost:7171/mcp` etc. Both protocol inspectors make the protocol calls from their own backend (the A2A Inspector's
FastAPI server, the MCP Inspector's Node proxy), not from the browser, so those calls start inside a container where
`localhost` is the container itself. Redis (`redis:8.8.3-alpine`, no password, AOF) is unpublished and reachable only
as `redis:6379` on the compose network. See proposal.md for the motivation.

## Goals / Non-Goals

**Goals:** the three inspectors up with `make`; host URLs work inside them on macOS and Linux; loopback-only exposure; CI
untouched.

**Non-Goals:** pre-configured connections or tokens inside the A2A and MCP inspectors (the developer picks a
persona/partner);
persisting the MCP Inspector's server list or secrets across restarts; the MCP Inspector's Apps tab (its sandbox
listener is not published); adding the inspectors to the topology report or diagram; a password or ACL on Redis;
persisting Redis Insight's Workbench history across restarts.

## Decisions

- **Share the balancer's network namespace (`network_mode: service:lb`).** Inside that namespace `localhost` is the
  balancer, so with nginx also listening on `7171` there, `http://localhost:7171/...` resolves exactly as on the host.
  Works identically on Docker Desktop for macOS and on Linux.
  *Alternatives:* `network_mode: host` — Linux only by default, needs an opt-in setting on Docker Desktop, and exposes
  the inspectors' listeners on every host interface. `extra_hosts`/DNS aliases — cannot redirect `localhost`. Rewriting
  the agent card's URL per caller — changes a signed card for a dev tool.
- **Ports are published by `lb`.** A container in another's namespace cannot publish ports; `lb` gains
  `127.0.0.1:7172:7172` and `127.0.0.1:7173:7173`. Its existing `7171:80` is unchanged. Inside the namespace each
  inspector binds `0.0.0.0` on its own port; only loopback reaches it from the host.
- **nginx `listen 7171;` in addition to `listen 80;`.** Same server block, same routes; only reachable inside the
  namespace (not published). `X-Forwarded-Host` (`$host:$server_port`) then reads `localhost:7171` for inspector
  traffic, which is the address the caller used.
- **A2A Inspector: built by compose from the official repository at a pinned commit** —
  `build.context: https://github.com/a2aproject/a2a-inspector.git#8aa064639af106ff771d60428ef6d460f5454743`. The project
  publishes no image; BuildKit fetches the git context on both platforms. Command overridden to
  `uvicorn app:app --host 0.0.0.0 --port 7172`. Healthcheck: a Python `urllib` GET of `http://127.0.0.1:7172/`
  (the image has no curl/wget).
- **MCP Inspector: `ghcr.io/modelcontextprotocol/inspector:2.9.0`** (multi-arch amd64/arm64, verified tag), with
  `CLIENT_PORT=7173`. The image already binds `0.0.0.0` with `DANGEROUSLY_BIND_ALL_INTERFACES=true` and has its own
  `HEALTHCHECK`. Its default origin allow-list for an all-interfaces bind includes `http://localhost:7173` and
  `http://127.0.0.1:7173`. Auth stays on: the served page carries the API token, so the browser needs nothing extra.
  No volume: the server list and any secrets stay in memory for the container's life, rather than in a plaintext
  `secrets.json` on a volume.
- **Defaults without forking the inspectors.** A2A: `compose/a2a-inspector/lab_app.py` is mounted read-only and run
  by uvicorn in place of upstream's `app:app`; it imports that app, swaps only its `/` route for the same page plus
  `<script src="/lab/defaults.js">`, and adds `/lab/token?agent=assistant|compliance`, which posts the dev client
  credentials (from compose's environment, the same defaults the api uses) to the lab's token endpoint and returns a
  fresh token, uncached. The script fills the card URL, selects Bearer and fills the token, and refetches it when the
  URL changes agent. Same origin, so no CORS change on the lab. MCP: `compose/mcp-inspector/start.mjs` (entrypoint)
  mints a dev token for a configurable persona (default `adam`, ADVISOR, firm-a), writes the catalog with the three
  servers (`streamable-http`, `protocolEra: auto`, `headers.Authorization`), rewrites it hourly (tokens last 8 h), and
  execs `mcp-inspector --web --catalog`. The catalog stays inside the container.
  *Alternatives:* forking/patching the inspectors' sources (drifts from the pin); a long-lived dev token (changes the
  issuer for a dev tool); a static catalog in the repo (would hold an expiring credential).
- **Profile `inspectors`, enabled by the Makefile.** `export COMPOSE_PROFILES := inspectors` unless `CI_MODE=1`.
  Exported, so the scripts' own `docker compose` calls (`wait_healthy.sh`, `dev.sh`) see the same set. Both services
  `depends_on: lb: service_healthy`.
- **UI links are built from the page's own host.** `${location.protocol}//${location.hostname}:<port>` rather than a
  fixed `localhost`, so the links follow however the lab was opened. They are plain `<a target="_blank"
  rel="noopener noreferrer">` after the router links (not `NavLink`: they leave the app), with a `↗` sign and an
  `aria-label` "… (opens in a new tab)". Shown always, including in CI mode where the inspectors do not run: the UI
  cannot know the profile, and a dead link in a model-free CI stack harms nothing.
- **`make dev`** stops `lb`; `scripts/dev.sh` also stops the inspectors, which would otherwise sit in a dead namespace.

- **Redis Insight: `redis/redisinsight:3.8.0`, on the compose network, not in `lb`'s namespace.** It needs only
  `redis:6379`, which resolves by service name, so it publishes `127.0.0.1:7174:7174` itself. `RI_APP_PORT=7174` keeps
  the port the same inside and out. Pre-connected by `RI_REDIS_HOST=redis`, `RI_REDIS_PORT=6379`,
  `RI_REDIS_ALIAS=maf-lab`; `RI_ACCEPT_TERMS_AND_CONDITIONS=true` accepts the licence without a dialog and leaves
  analytics and notifications off; `RI_FILES_LOGGER=false` keeps logs on stdout. The image has no `HEALTHCHECK`, so
  compose probes its health endpoint with Node's `fetch` (the image has Node, no curl/wget); the exact path is
  confirmed during apply. No volume, as for the MCP Inspector: the connection is recreated from the environment on each
  start, and only Workbench history is lost. `depends_on: redis: service_healthy`.
  *Alternatives:* redis-commander (env-configurable, but slow-moving and thinner: no profiler, weak streams),
  p3x-redis-ui (MIT, but connections only from its GUI or a JSON file it says not to hand-write), phpRedisAdmin (basic
  browse/edit only). Redis Insight is the official one, like the other two inspectors.

## Risks / Trade-offs

- [`lb` is recreated (config or image change)] → compose recreates the containers that use its namespace on the next
  `up`; a manual `docker restart lb` leaves them without network until `make up`. Documented.
- [First `make` builds the A2A Inspector from GitHub (~1–2 min, needs network)] → cached afterwards; the pin means it
  rebuilds only when the commit in compose changes.
- [Port 7172/7173 already taken on a developer machine] → `make up` fails naming the port, like 7171 today.
- [MCP Inspector backend can spawn stdio servers inside its container] → loopback-only, token-protected page, and the
  container has no host mounts.
- [Redis Insight has no login and can write or flush keys] → loopback-only; the lab's Redis holds dev state that a
  `make` rebuilds. Documented in README next to the URL.
- [Redis Insight is SSPL-licensed with an EULA] → used as a local developer tool, not shipped; recorded in DECISIONS.
- [A future Redis Insight may ask for the licence again after an agreements update] → the tag is pinned; an upgrade
  is a deliberate change that checks it.

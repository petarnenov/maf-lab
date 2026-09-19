# Design

## Context

See proposal.md for motivation. What exists to build on:

- `InstanceIdentity` already gives both hosts an anonymous `GET /health` returning `{status, instance}`, and stamps
  `X-Instance` on every response. The balancer does **not** expose `/health`, so it is an inside-the-network route.
- Docker DNS resolves a compose service name to *every* replica's address — that is how the balancer finds its
  upstreams. So an api replica can discover its peers, and the mcp replicas, without a registry.
- `AdminJobRow` already carries `OwnerInstance` and `HeartbeatAt`, but it only knows replicas that ran an admin job.
- The api holds a `QdrantClient` and `ModelProviders` in DI, and reaches MCP through `http://lb/mcp`. Chat is Ollama
  Cloud (`Models:ChatEndpoint`, key from `OLLAMA_API_KEY`); embeddings are the local Ollama.
- Compose healthchecks exist per service, but they are TCP probes read by Docker, not by the api.
- The web app has no SVG, chart or graph dependency, four npm dependencies in total, and a shared `Page.module.css`;
  pages fetch with `useApi()` + TanStack Query and are tested with a stubbed `fetch`.

## Goals / Non-Goals

**Goals:**
- One page that shows the real stack: what is up, how many replicas, what they are called, how big the index is.
- The picture stays hand-drawn — a person decides the layout in draw.io — while the state comes from the system.
- No new runtime dependency, and no way for the diagram and the system to drift apart unnoticed.

**Non-Goals:**
- A monitoring product: no history, no time series, no alerting, no per-request tracing (the monitor already does
  per-turn tracing).
- Editing the diagram in the browser, or generating the layout automatically.
- Reporting anything about tenants, conversations or documents — infrastructure only.
- Controlling the stack from the page (no restart, no scale).

## Decisions

### The `.drawio` file is parsed, not exported
`docs/topology.drawio` is authored as **uncompressed** mxGraph XML (draw.io's "compressed" saving turns the diagram
into a base64 blob, which is unreviewable and unparsable). The file is served to the web app, which reads each
`mxCell`'s geometry, label, style and `source`/`target`, and renders its own SVG from them. So the diagram decides
where a box is, what it is called and which arrows exist; the app decides how a box *looks* once state is known.

Alternatives: exporting an SVG from draw.io and showing it as an image (no way to overlay per-node state without
fragile DOM surgery, and the export is a manual step that silently rots); generating the whole diagram from the
report (always correct, never legible); embedding draw.io's viewer (a heavy third-party script for a page that must
work offline in a lab).

A node's `id` in the diagram is the contract: it must equal the service id in the report, and a test asserts the two
sets are equal in both directions. That test also fails if the file was saved compressed.

### `GET /api/topology`: probe in parallel, cache briefly
A `TopologyProbe` service asks each dependency the cheapest question that proves it works, all at once, each with its
own short timeout (2 s), and never lets one failure fail the report:

| Node | Probe | Degraded when |
|---|---|---|
| `lb` | `GET /lb-health` | — |
| `api` | `GET /health` on every replica address DNS returns | some replicas answer, not all |
| `mcp` | `GET /health` per replica address, plus one `tools/list` through the balancer | a replica is silent, or fewer tools than expected |
| `qdrant` | collection info through the `QdrantClient` already in DI | the expected collection is missing |
| `ollama-embeddings` | `GET /api/tags` on `Models:OllamaEndpoint` | a configured embedding model is not pulled |
| `chat-provider` | no network call: configuration only (endpoint, model, whether a key is set) | remote endpoint with no key |
| `web` | `GET /healthz` | — |

The chat provider is deliberately *not* probed: it is Ollama Cloud, a paid remote endpoint, and pinging it on every
page refresh to learn what configuration already says is waste. The page states that its health is "not probed".

The result is cached for 5 seconds (`IMemoryCache`), so a page polling every 5 s and two replicas behind a balancer
cannot turn into a probe storm.

### Replicas are discovered by DNS and asked directly
`Dns.GetHostAddressesAsync("api")` inside the compose network returns one address per replica — the same fact nginx
relies on. The probe resolves the api and mcp service names, then asks each address its own `GET /health`, which
answers with that container's instance name. So every replica is reported by name with its own health, whether or
not it has ever served a request, and a replica that died is simply absent from DNS.

Outside compose (`make dev`, tests, a single process) the names do not resolve. That is not an error: the api then
reports only itself, and the mcp entry falls back to the balancer's `tools/list`, whose response header names the
replica that answered. The service names are configuration (`Topology:ApiService`, `Topology:McpService`), empty
disables discovery.

Alternatives rejected: **heartbeat rows in the shared SQLite** (a new table and a timer to learn something DNS
already knows, and it reports replicas that once ran, not replicas that work); **observing `maf-lab/instance` from
tool results** (only populated while `Agent:TraceRetrieval` is on, and it says "seen", not "running"); **the Docker
socket** (reports containers rather than working services, and mounting the socket into the api is a real privilege
for a cosmetic feature).

### The page renders SVG itself
`web/src/topology/` gets three pieces: a parser (mxGraph XML → nodes and edges), a renderer (SVG from those, with
`viewBox` from the diagram's extent so it scales to the pane), and the page (query, refresh, selection, detail).
Health is shown by **both** colour and a shape/label — a dot with a distinct symbol and the state spelled out — so
the page does not rely on colour alone. State is fetched with TanStack Query, `refetchInterval` 5 s while the page is
open, and the header says when the state was last refreshed.

The diagram is loaded as a static asset over HTTP (not bundled with `?raw`), so editing the file and reloading the
page is enough to see the change while the stack runs.

## Risks / Trade-offs

- **Hand-written mxGraph XML is fiddly** → the file stays small (a dozen nodes), the parser ignores everything it
  does not understand, and the parser is unit-tested against the committed file.
- **DNS discovery works only inside the compose network** → outside it the report degrades to "this instance", which
  is exactly what a single-process dev run is; the page says where the list came from.
- **A replica can answer `/health` while being useless** (e.g. its Qdrant is down) → `/health` is liveness; the
  dependency has its own node in the picture, which is the point of drawing the whole stack.
- **A probe could become a load generator** → 5 s cache, 2 s timeout, no probe of the paid chat endpoint.
- **The drift test is only as good as the ids** → the ids are the only thing it compares, and both sides are small.

## Migration Plan

Additive and stateless: a new endpoint, a new page, a new static asset. No schema change, no background writer,
nothing existing changes behaviour. Rolling back is removing the route and the endpoint.

## Open Questions

- Whether the embeddings probe should also check that each configured model is actually pulled, or only that Ollama
  answers — decided in implementation from what `/api/tags` costs; either way the contract ("degraded, with a
  reason") is unchanged.

# Proposal

## Why

The stack has no telemetry. What it knows about itself today is `/health` per instance, a topology probe that
measures its own round trip, structured logs on each container's stdout, and the per-turn trace — which is a
product feature for the person who asked the question, not something an operator can aggregate over. There is no
way to ask how long a model call takes at the 95th percentile, how many tokens a day costs, which tool fails most,
or where the time in a slow turn actually went across api, MCP and Qdrant.

Microsoft Agent Framework already carries the instrumentation for the hardest part of that: `Microsoft.Extensions.AI`
ships `OpenTelemetryChatClient` and `UseOpenTelemetry()`, and `Microsoft.Agents.AI` ships `OpenTelemetryAgent` with
the same extension. Both follow the GenAI semantic conventions. Nothing in this repository uses them.

## What Changes

- Every .NET service — api, mcp-retrieval and the compliance agent — emits OTLP traces, metrics and logs, with
  `service.name` and `service.instance.id` set from the instance identity the lab already uses for load balancing.
- The agent and every model call are instrumented through the framework's own `UseOpenTelemetry()`, not through
  spans written by hand. ASP.NET Core, `HttpClient` and EF Core use their standard instrumentation. What is left
  to write by hand is only what no library covers: the MCP tool call, the Qdrant query and the BM25 encode.
- The browser sends its own spans for a chat run, and the run's trace context travels from the browser through the
  api to the MCP server and back on the SSE stream, so one turn is one trace end to end.
- Three services join the stack: an OpenTelemetry Collector, Prometheus for metrics and Jaeger for traces. They are
  services of the lab like any other, so they appear in the topology report and in the drawn diagram.
- A new `/telemetry` screen shows the stack's own numbers — run and turn rates, model latency and token usage,
  tool call outcomes, retrieval latency, error rates, per-instance spread — read from Prometheus through the api,
  with a link from any turn to its trace in Jaeger.
- **BREAKING for operators**: the topology page stops showing the probe's own round-trip latency, which measured
  one request nobody made. It shows the real latency of the traffic the service is serving, out of OTel metrics.
- Logs leave through OTLP as well as the console, so a turn's logs sit beside its spans. The rule that no message
  content is ever logged stays exactly as it is, and extends: no prompt, answer, reasoning or document text may
  reach any telemetry signal.

## Capabilities

### New Capabilities

- `telemetry`: what the stack emits about itself — the signals, who emits them, what must never appear in them,
  where they go and how a turn is followed across services.

### Modified Capabilities

- `web-ui`: a new requirement for the telemetry dashboard screen.
- `system-topology`: the report's list of services gains the Collector, Prometheus and Jaeger, and the diagram
  gains them with it.

## Impact

- `Directory.Packages.props`, `DECISIONS.md` — the OpenTelemetry packages and why each is there.
- `src/Maf.Lab.Hosting/` — one place that wires the signals for every service, beside `InstanceIdentity`.
- `src/Maf.Lab.Api/Program.cs`, `Agent/ChatTurnRunner.cs` — `UseOpenTelemetry()` on the agent and the chat client.
- `src/Maf.Lab.Retrieval/` — spans for the MCP tool call, the Qdrant query and the BM25 encode.
- `src/Maf.Lab.ComplianceAgent/Program.cs` — the same shared wiring.
- `src/Maf.Lab.Api/Topology/` — the three new services, and the latency that is no longer probed.
- `src/Maf.Lab.Api/Endpoints/` — a read-only Prometheus query endpoint for the dashboard.
- `compose/docker-compose.yml`, `compose/lb/nginx.conf`, `compose/otel/` — the new services and their routes.
- `docs/topology.drawio` — the three new nodes; a test refuses a diagram that does not match the report.
- `web/` — the `/telemetry` screen, and browser tracing on the chat page.
- `Makefile`, `docs/http-api.md` — the new endpoints and how to reach Jaeger and Prometheus.

## Assumptions

- The per-turn trace stays exactly as it is. It is a product feature with its own retention, access scope and UI,
  and OTel spans deliberately carry no prompts, answers or document text — so it is not a duplicate of anything
  OpenTelemetry offers, and removing it would remove the behind-the-scenes panel.
- Telemetry is off by default in tests and on by default in compose, so the test suite neither exports nor needs a
  collector.

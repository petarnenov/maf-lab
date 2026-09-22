# Design

## Context

See proposal.md — Why. What shapes the approach:

- **What exists.** `/health` per instance and `X-Instance` on every response (`Maf.Lab.Hosting.InstanceIdentity`,
  shared by every service). `TopologyProbe` contacts each service and records its own round-trip in
  `TopologyNode.LatencyMs`. `ToolAudit` and `AuditChain` write business records to SQLite. `TurnTrace` records the
  per-turn trace shown behind the scenes. There is no `Meter`, no `ActivitySource` and no exporter anywhere.
- **What the framework brings.** The installed `Microsoft.Extensions.AI` 10.10.0 carries `OpenTelemetryChatClient`
  and `UseOpenTelemetry()`; `Microsoft.Agents.AI` 1.22.0 carries `OpenTelemetryAgent` and its own
  `UseOpenTelemetry()` builder extension. Both follow the GenAI semantic conventions and both can be asked to
  record message content — which this system must not do.
- **Where a chat turn already composes.** `ChatTurnRunner` builds the chat client
  (`TracingChatClient` → `RequiredToolModeChatClient` → provider) and the agent
  (`ChatClientAgent` → `.AsBuilder().Use(middleware)`), so both have an obvious place for one more decorator.
  The MCP call goes out through `McpToolSource`; `Maf.Lab.Retrieval` serves it and queries Qdrant.
- **The diagram is coupled to the report.** A test refuses a `docs/topology.drawio` whose nodes do not match the
  topology report, so adding services to the report means editing the diagram in the same change.
- **The load balancer is the only way in.** Everything is reached through nginx on 7171, which routes `/api/`,
  `/mcp`, `/a2a`, `/compliance` and `/`.

## Goals / Non-Goals

**Goals:**

- One turn, one trace, from the browser to the vector store.
- The framework's instrumentation used as it is, with hand-written spans only where nothing exists.
- An operator's view of the stack that is read from the signals, not from a second set of counters.

**Non-Goals:**

- Replacing the per-turn trace, the audit chain or the compliance record. Two are product, one is evidence; none
  of them is telemetry (see proposal.md — Assumptions).
- Alerting, dashboards as code, or a retention policy for the metrics store beyond what a lab needs.
- Instrumenting the eval CLI or the indexer.

## Decisions

### Collector in the middle, Prometheus and Jaeger behind it

Services speak OTLP to one Collector; the Collector exports metrics to Prometheus and traces to Jaeger. Nothing in
the application knows which backend is in use, which is the point of the Collector and what makes the choice of
backend a compose file's business rather than a code change.

*Alternative:* export straight to Prometheus and Jaeger from each service. Rejected — it puts the backend's
identity into every service's configuration and gives up the one place to batch, retry and drop.

### One wiring, in `Maf.Lab.Hosting`

`InstanceIdentity` already lives there because every service needs it; the OTel wiring joins it as
`AddLabTelemetry(...)`: resource attributes (`service.name`, `service.instance.id` = `InstanceIdentity.Name`),
the OTLP exporters, the standard ASP.NET Core / HttpClient / EF Core instrumentation, and the lab's own
`ActivitySource` and `Meter` names. Each service calls it once with its own name.

Off when no endpoint is configured, so tests and `make dev` run untouched.

### The agent and the model are decorated, not re-measured

`.UseOpenTelemetry()` goes on the chat client beside `TracingChatClient`, and on the agent in the builder that
already carries the tool middleware. `EnableSensitiveData` is never set, so no prompt or completion is recorded.
`TracingChatClient` stays: it writes the *product* trace, which carries what OTel deliberately will not.

The two are not a duplicate measurement of each other — one produces a metric an operator aggregates, the other a
step a user reads — but the hand-rolled aggregates that OTel now covers go: `TopologyNode.LatencyMs` is removed
and the topology page reads the real latency of served traffic from the metrics instead of the probe's own.

### Propagation reaches the browser and the MCP server

W3C `traceparent` is the default propagator. Outbound HTTP is instrumented, so the MCP call carries it already.
The browser gets `@opentelemetry/sdk-trace-web` with a fetch instrumentation that propagates to `/api/chat`, and
the api reports the run's trace id to the client so a turn can be opened in Jaeger from the chat screen.

*Alternative:* leave the browser out and start the trace at the api. Rejected — the user asked for the web app to
be instrumented, and the time between "Send" and the first token is only visible from there.

### The dashboard reads Prometheus through the api

The page never talks to Prometheus directly: the api exposes a read-only, signed-in endpoint that runs a fixed set
of named queries against Prometheus and returns their results. Nothing from the caller reaches Prometheus as a
query, so the screen cannot be turned into an arbitrary query runner, and CORS stays a non-problem. Jaeger is
opened by link, through the load balancer.

*Alternative:* let the browser query Prometheus directly. Rejected — it would put Prometheus on the public route
with no authorisation and let any caller write any query.

### What is measured, and by whom

| Signal | From |
|---|---|
| model call duration, token usage | `Microsoft.Extensions.AI` GenAI instrumentation |
| agent run duration | `Microsoft.Agents.AI` instrumentation |
| HTTP server and client, EF Core | standard instrumentation packages |
| turn started / finished / failed / awaiting a person, turn duration | the lab's own `Meter`, in `ChatTurnRunner` |
| tool calls by tool and outcome | the lab's own `Meter`, where the audit row is already written |
| retrieval duration by stage (embed, sparse, qdrant, rerank) | the lab's own `Meter` and spans, in `Maf.Lab.Retrieval` |

The lab's own instruments are named under one prefix so they are recognisable in Prometheus beside the
convention-named ones.

### Content stays out, by construction

The rule is already "logs carry structure, never message content". It now covers every signal. Concretely: the
GenAI instrumentation's sensitive-data switch is never enabled; hand-written spans take identifiers, names and
counts only; the OTLP log exporter carries the same `ILogger` records that are already content-free; and a test
runs a turn with marker strings in the question, the answer and a document, then asserts that no exported span,
metric or log carries any of them.

## Risks / Trade-offs

- **Three more containers on a laptop** → they are small, they are in the same compose file as everything else,
  and `make` brings them up with the rest. A run without them still works, because telemetry is off when there is
  no endpoint.
- **The GenAI conventions are still moving** → the framework owns that surface; this system pins the packages, and
  a version move already requires a DECISIONS.md note.
- **Browser tracing adds weight to the bundle** → only the chat page loads it, and the exporter endpoint is the
  same load balancer the page already talks to.
- **A dashboard can imply more certainty than a lab's traffic deserves** → every number says which period it
  covers, and a period with no data says so rather than showing a zero.
- **The topology page loses a number it used to show** → it showed one probe's round trip, which measured a
  request nobody made; the replacement is the latency of real traffic.

## Migration Plan

Additive except for the probe latency. The three services are new; nothing that exists changes shape. With no
OTLP endpoint configured — tests, `make dev` — the system behaves exactly as before. Rollback is removing the
services from compose and unsetting the endpoint; the code stays, dormant.

## Open Questions

- Which retention Prometheus and Jaeger keep for a lab. A default is enough to start; it changes nothing in the
  specs or the task breakdown.

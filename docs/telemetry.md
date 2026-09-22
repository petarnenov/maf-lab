# Telemetry

What the stack emits about itself, and where it goes. The per-turn trace behind the chat screen is a different
thing: it is the product, it carries prompts and answers, and it lives under its own retention and access rules
(see [trace-events.md](trace-events.md)). Nothing here carries message content.

## The signals

Every .NET service — api, mcp-retrieval, compliance — calls `AddLabTelemetry(<service name>)` once and emits
traces, metrics and logs over OTLP. Every signal carries `service.name` and `service.instance.id`, which is the
instance name the load balancer already reports, so a number can be attributed to the replica that produced it.

Configured by `Telemetry:Endpoint`. **Unset it and nothing is exported** — which is what the tests and `make dev`
do, so neither needs a collector.

## Where it comes from

| Signal | Written by |
|---|---|
| model call spans, `gen_ai.client.operation.duration`, `gen_ai.client.token.usage` | `Microsoft.Extensions.AI` (`OpenTelemetryChatClient`) |
| agent run spans (`invoke_agent`, `gen_ai.agent.*`) | `Microsoft.Agents.AI` (`OpenTelemetryAgent`) |
| incoming requests, outgoing HTTP, EF Core | the standard OpenTelemetry instrumentation |
| `tool.call`, `mcp.tool`, `retrieval.embed`, `retrieval.sparse_encode`, `retrieval.query`, `retrieval.rerank` | this system, because no library writes them |
| `maf.turns`, `maf.turn.duration`, `maf.tool.calls`, `maf.retrieval.stage.duration` | this system's own `Meter` |

`maf.turns` is tagged `outcome` = `started`, `answered`, `failed` or `awaiting_person` — a turn that stopped for
an advisor's approval is not a failure. `maf.tool.calls` is tagged `tool.name` and `outcome`, and is counted where
the audit row is written so the two can never disagree.

## No content, anywhere

No prompt, question, answer, reasoning, document text, snippet or free-text tool argument appears in a span, a
metric or a log. The GenAI instrumentation can be asked to record prompts and completions; it never is, and
turning it on is not a supported configuration of this system. A test runs a turn with markers in the question,
the answer, the reasoning and a document and refuses any that reaches an exported signal.

What a span does carry, and should: `gen_ai.tool.description` holds the tool's own description — the text this
system wrote to tell the model what a tool is for. It is configuration, not anybody's data, and the turn trace
already shows it. Searching an exported trace for a phrase that appears in a tool description will find it there.

## Where the signals go

Services speak OTLP to one collector, which exports metrics to Prometheus and traces to Jaeger. The services know
only the collector's address: which backend keeps what is decided in `compose/docker-compose.yml`, not in any
service's configuration.

Through the load balancer on `http://localhost:7171`:

- `/telemetry` — the screen, which reads the numbers through the api
- `/jaeger` — the trace store; a turn also links straight to its own trace
- `/v1/traces` — where the browser's own spans go

One turn is one trace: the browser starts it, `traceparent` carries it to the api, the api's HTTP client carries
it to the MCP server, and the MCP server hangs its work under it rather than starting a trace of its own.

## Reading the numbers

`GET /api/telemetry?window=…` runs a fixed set of queries against Prometheus and returns their results. The
caller picks the period and nothing else, so the screen is not a way to run arbitrary queries, and Prometheus
never has to be reachable from a browser. See [http-api.md](http-api.md).

# Telemetry

What the stack emits about itself, and where it goes. The per-turn trace behind the chat screen is a different
thing: it is the product, it carries prompts and answers, and it lives under its own retention and access rules
(see [trace-events.md](trace-events.md)). Nothing here carries message content.

## The signals

Every .NET service — api, mcp-retrieval, compliance — calls `AddLabTelemetry(<service name>)` once and emits
traces, metrics and logs over OTLP. Every signal carries `service.name` and `service.instance.id`, which is the
instance name the load balancer already reports, so a number can be attributed to the replica that produced it.

Configured by `Telemetry:Endpoint`, which `compose/env/platform.env` defaults to the collector's address. **Unset it
and nothing is exported** — which is what the tests and `make dev` do, so neither needs a collector.

The collector, the metrics store, the trace store, the Telemetry screen and a turn's link to its trace are the
`observability` plugin's. Without that plugin installed, the default address names no service: the exports go
nowhere, quietly — the OTLP exporter's own behaviour (no log line, no retry, a bounded queue) — and a turn carries no
trace link. Set `TELEMETRY_ENDPOINT=` to switch export off altogether. The plugin's own documentation says where the
signals go and how the screen reads them.

## Where it comes from

| Signal | Written by |
|---|---|
| model call spans, `gen_ai.client.operation.duration`, `gen_ai.client.token.usage` | `Microsoft.Extensions.AI` (`OpenTelemetryChatClient`) |
| agent run spans (`invoke_agent`, `gen_ai.agent.*`) | `Microsoft.Agents.AI` (`OpenTelemetryAgent`) |
| incoming requests, outgoing HTTP, EF Core | the standard OpenTelemetry instrumentation |
| `tool.call`, `mcp.tool`, `retrieval.embed`, `retrieval.sparse_encode`, `retrieval.query`, `retrieval.rerank`, `graph.read` (named query, rows, truncated, duration; the same numbers reach the turn trace's `graph` event, see [trace-events.md](trace-events.md)), `graph.write` | this system, because no library writes them |
| `maf.turns`, `maf.turn.duration`, `maf.tool.calls`, `maf.retrieval.stage.duration`, `maf.runner.reuse`, `maf.graph.query.duration` (by named query and outcome) | this system's own `Meter` |

`maf.turns` is tagged `outcome` = `started`, `answered`, `failed` or `awaiting_person` — a turn that stopped for
an advisor's approval is not a failure. `maf.tool.calls` is tagged `tool.name` and `outcome`, and is counted where
the audit row is written so the two can never disagree. `maf.runner.reuse` counts the coverage runner's requests by
what result reuse did with them, tagged `outcome` (`hit`: answered with a kept result, `joined`: waited for an
identical job in flight, `miss`: ran and may be kept, `fresh`: asked for its own run, `off`: not eligible), `toolchain`
and `scope`. It never carries the commit, the diff or a path, and neither does the runner's log line for it.

## No content, anywhere

No prompt, question, answer, reasoning, document text, snippet or free-text tool argument appears in a span, a
metric or a log. The GenAI instrumentation can be asked to record prompts and completions; it never is, and
turning it on is not a supported configuration of this system. A test runs a turn with markers in the question,
the answer, the reasoning and a document and refuses any that reaches an exported signal.

A test-generation run's activity — the test agent's text and reasoning, and which files its tools touched — is
content in the same sense. It is kept in the api's database for the Coverage screen, which reads it over the run's
AG-UI stream, and it is never emitted as a span, a metric or a log line; a test runs the agent with markers in its
text and reasoning and refuses any that reaches its logs or spans.

What a span does carry, and should: `gen_ai.tool.description` holds the tool's own description — the text this
system wrote to tell the model what a tool is for. It is configuration, not anybody's data, and the turn trace
already shows it. Searching an exported trace for a phrase that appears in a tool description will find it there.

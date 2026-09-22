# Tasks

## 1. The signals, wired once

- [x] 1.1 Pin the OpenTelemetry packages in `Directory.Packages.props` (SDK, OTLP exporter, hosting extensions,
      ASP.NET Core / HttpClient / EF Core instrumentation) and record each one and why in `DECISIONS.md`; verify
      `make lint-dotnet` builds clean.
- [x] 1.2 Add `AddLabTelemetry(serviceName)` to `src/Maf.Lab.Hosting/`: resource attributes with
      `service.instance.id` from `InstanceIdentity.Name`, OTLP exporters for traces, metrics and logs, the standard
      instrumentation, and the lab's own `ActivitySource`/`Meter` names; verify a unit test asserts the resource
      carries the service and instance names.
- [x] 1.3 Make it a no-op when no OTLP endpoint is configured; verify a test starts the api with no endpoint,
      runs a turn and asserts nothing is exported and the turn is unaffected.
- [x] 1.4 Call it from `Maf.Lab.Api`, `Maf.Lab.Retrieval` and `Maf.Lab.ComplianceAgent`; verify each service starts
      with telemetry configured and its existing tests still pass.

## 2. The framework's own instrumentation

- [x] 2.1 Put `UseOpenTelemetry()` on the chat client in `ChatTurnRunner` beside `TracingChatClient`, leaving
      sensitive-data capture off; verify a test asserts a model call produces a GenAI span with a duration and
      token metrics, and that the span holds no prompt or completion.
- [x] 2.2 Put `UseOpenTelemetry()` on the agent in the builder that already carries the tool middleware; verify a
      test asserts the agent run's span is the parent of the model call's.
- [x] 2.3 Write the spans nothing covers — the MCP tool call in the api, and the tool call, the Qdrant query and
      the BM25 encode in `Maf.Lab.Retrieval`; verify a test asserts the four spans and their parent-child order.
- [x] 2.4 Confirm no measurement is taken twice: remove `TopologyNode.LatencyMs` and the probe's round-trip timing;
      verify the topology tests no longer expect it.

## 3. One turn, one trace

- [x] 3.1 Confirm W3C propagation from api to MCP over the instrumented `HttpClient`, and to the compliance agent
      over A2A; verify an integration test asserts one trace id spans api, MCP and the compliance agent.
- [x] 3.2 Report the run's trace id to the client on the run's events; verify a test reads it off the stream and a
      web test stores it on the turn.
- [x] 3.3 Add browser tracing to the chat page (`@opentelemetry/sdk-trace-web` + fetch instrumentation, exporting
      to the collector through the load balancer) so the run's span starts at "Send"; verify a web test asserts the
      request carries `traceparent` and that tracing stays out of the way when the endpoint is not configured.

## 4. What the lab measures itself

- [x] 4.1 Add the turn instruments in `ChatTurnRunner` — started, finished, failed, awaiting a person, and turn
      duration; verify a test runs a failing turn, a paused turn and a normal turn and asserts each is counted once
      under the right outcome.
- [x] 4.2 Add the tool-call instrument where the audit row is already written, by tool and outcome; verify a test
      asserts a failed call and an unknown tool are counted apart from the calls that worked.
- [x] 4.3 Add the retrieval instruments in `Maf.Lab.Retrieval`, split into embed, sparse encode, Qdrant and rerank;
      verify a test asserts each stage is recorded for one search.
- [x] 4.4 Assert the content rule across every signal: a turn with marker strings in the question, the answer, the
      reasoning and a document leaves no marker in any exported span, metric or log.

## 5. The stack the signals go to

- [x] 5.1 Add the collector, Prometheus and Jaeger to `compose/docker-compose.yml` with a collector config under
      `compose/otel/`; verify `make` brings the stack up healthy with them.
- [x] 5.2 Route the collector's OTLP/HTTP endpoint and Jaeger's UI through `compose/lb/nginx.conf`; verify the
      browser can export and a person can open Jaeger through `http://localhost:7171`.
- [x] 5.3 Report the three services in the topology and add them to `docs/topology.drawio` with their edges; verify
      the diagram test passes and the topology report lists them.
- [x] 5.4 Add a read-only, signed-in endpoint that runs a fixed set of named queries against Prometheus; verify
      tests cover a known query, an unknown query name being refused, and Prometheus being unreachable.

## 6. The telemetry screen

- [x] 6.1 Add the `/telemetry` route and its navigation entry; verify a test asserts it is reachable and refused to
      a signed-out user.
- [x] 6.2 Render the turn, model, tool and retrieval numbers for a chosen period, with the per-instance spread and
      the period stated on each; verify tests cover a period with data and one without, which must read as no data
      rather than zero.
- [x] 6.3 Show the error state when the metrics cannot be read, keeping the screen usable; verify a test with the
      endpoint failing.
- [x] 6.4 Offer a link from a turn to its trace, from both the telemetry screen and the chat turn; verify a test
      asserts the link carries that turn's trace id.

## 7. Documentation and verification

- [x] 7.1 Document the endpoints, the signals, the instrument names and how to reach Jaeger and Prometheus in
      `docs/http-api.md` and `docs/topology.md` or equivalent; verify the documented shapes match what is served.
- [x] 7.2 Run `make lint` and `make test` and confirm they pass.
- [x] 7.3 Run the stack (`make`), ask a question, and confirm one trace in Jaeger holds the browser span, the api
      turn, the model calls, the MCP call and the Qdrant query, and that the `/telemetry` screen shows the turn.
- [x] 7.4 Confirm by inspection of an exported trace that no prompt, answer, reasoning or snippet appears in it.

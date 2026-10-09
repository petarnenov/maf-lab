# Telemetry: the stack and the screen

The observability plugin's part of [the lab's telemetry](../../../docs/telemetry.md): what the services emit is the
core's; where it goes and how it is read is this plugin's. Its services are `otel-collector`, `prometheus` and `jaeger`
(`compose.yml`), its balancer routes `/v1/traces` and `/jaeger` (`lb.server.conf`), and its api part serves the screen's
numbers and a turn's trace link (`server/`).

## Where the signals go

Services speak OTLP to one collector, which exports metrics to Prometheus and traces to Jaeger. The services know
only the collector's address: which backend keeps what is decided in this plugin's `files/otel/collector.yaml`, not
in any service's configuration.

Through the load balancer on `http://localhost:7171`:

- `/platform?section=telemetry` — the platform dashboard section for `PLATFORM_ADMIN`; `/telemetry` also opens its guarded standalone screen
- `/jaeger` — the trace store; a turn also links straight to its own trace
- `/v1/traces` — where the browser's own spans go

One turn is one trace: the browser starts it, `traceparent` carries it to the api, the api's HTTP client carries
it to the MCP server, and the MCP server hangs its work under it rather than starting a trace of its own.

## Reading the numbers

`GET /api/platform/telemetry?window=…` runs a fixed set of queries against Prometheus and returns their results. The
caller picks the period and nothing else, so the screen is not a way to run arbitrary queries, and Prometheus
never has to be reachable from a browser. See [http-api.md](http-api.md).

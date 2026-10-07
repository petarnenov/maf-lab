The Telemetry screen's numbers, while the observability plugin is installed (otherwise `404`). See
[telemetry.md](telemetry.md) for where the signals go.

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/telemetry?window=15m\|1h\|6h\|24h` | — | `TelemetryReport` — any signed-in user. A window not on that list is `400` |

`TelemetryReport` = `{ window, generatedAt, available, reason, panels: TelemetryPanel[], traceUrl }`.
`TelemetryPanel` = `{ id, title, unit, series: { label, value }[] }`; an empty `series` means nothing was measured
in the window, which is not the same as zero. `available: false` with a `reason` means the metrics store could not
be read.

The queries behind the panels live in the plugin's server part, in the api. A caller chooses the period and nothing
else, so this is not a way to run arbitrary queries against the metrics store, and the store is never reachable from a
browser.

Through the load balancer: `/jaeger` opens the trace store, and `/v1/traces` is where the browser's own spans go.

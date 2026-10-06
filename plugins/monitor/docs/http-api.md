A turn's behind-the-scenes trace never travels on the run's stream (agui-protocol-only). While the monitor is
installed, the run's owner reads it while the run is going, from any replica (the trace is kept in the shared store for
the run's grace period), and afterwards from the kept turn. The monitor's pane polls the first while it is open and the
run is live. See [trace-events.md](trace-events.md).

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/runs/{runId}/trace?after=` | — | `{ runId, turnId, ended, events: TraceEvent[] }` — the run's trace while it is written, events after `seq` `after`; the run's owner only, otherwise `404` |
| GET | `/api/turns/{turnId}/trace` | — | `{ turnId, conversationId, createdAt, events: TraceEvent[], aguiFrames: RunFrame[] \| null }` — the turn's owner, or a TENANT_ADMIN of the same tenant for turns in the review queue; otherwise `404`, as for a turn whose trace is no longer kept (`Tracing:RetentionDays`, 7). `aguiFrames` is the run's own events as they crossed the wire, and is `null` for a turn answered before they were kept |

`RunFrame` = `{ seq, atMs, type, bytes, payload?, truncated }` — see [trace-events.md](trace-events.md). Turns recorded
before agui-protocol-only may also carry `name` and `traceSeq` on their custom-event frames.

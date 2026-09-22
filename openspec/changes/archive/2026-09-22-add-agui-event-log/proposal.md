# Proposal

## Why

The behind-the-scenes panel shows what the *server* did for a turn, but nothing shows what actually crossed the
AG-UI wire. The web client discards most of the protocol on the way in: `web/src/chat/chatEvents.ts` maps six event
types and returns `[]` for everything else — `RUN_STARTED`, `TEXT_MESSAGE_START`/`END`, `TOOL_CALL_END`, `STEP_*`,
custom events under an unknown name, and any type a future SDK version adds. When a run renders wrongly there is no
way, short of the browser's network tab, to see whether the frame arrived, in what order, and with what payload.

## What Changes

- The web client captures every SSE frame of a run as it reads the stream — before any mapping, including frames
  whose types it does not handle and frames whose JSON does not parse — and keeps them per assistant turn.
- The behind-the-scenes panel gains a sixth tab, **AG-UI**, listing those frames as a flat, ordered list: sequence,
  arrival offset, the frame's event type, the custom event's name where it has one, size, and the raw payload
  expanded on demand. Every frame gets a row; nothing is coalesced. Opening the tab is all that turns it on —
  there is no separate record switch.
- The tab obeys the panel's existing rules: frames after the time-travel cursor are dimmed, the frame at it is
  highlighted, and a live run appends rows as they arrive.
- The API records the frames of every run it streams and stores them with the turn, so reopening a stored turn
  shows the same list. They live under the trace's retention (7 days), the trace's access scope, and their own
  size cap, and are returned by `GET /api/turns/{turnId}/trace` alongside the trace events.
- A `maf-lab/trace` custom frame is recorded by name, sequence and size rather than by copying its payload — the
  payload is the trace event the panel's other five tabs already render.
- A resume run (an answer to a confirmation) creates no turn, so its frames are visible live but not stored. The
  tab says so rather than showing an empty list.

## Capabilities

### New Capabilities

None. The behavior extends what `turn-tracing` already captures and `web-ui` already presents.

### Modified Capabilities

- `turn-tracing`: a new requirement to record the run's AG-UI frames, persist them with the turn under the same
  retention, access scope and truncation rules as the trace, and return them with it.
- `web-ui`: the Monitor views requirement gains the AG-UI tab, and its live/time-travel behavior is stated for it.

## Impact

- `web/src/chat/readChatStream.ts` — surfaces each raw frame alongside the mapped events.
- `web/src/chat/chatReducer.ts`, `web/src/monitor/traceReducer.ts` (or a sibling) — per-turn frame state.
- `web/src/monitor/MonitorPanel.tsx`, `MonitorTabs.tsx`, `MonitorPanel.module.css` — the new tab.
- `web/src/monitor/useTurnTrace.ts`, `web/src/api/types.ts` — the frames on the trace document.
- `src/Maf.Lab.Api/Endpoints/ChatEndpoints.cs` — the single point every event of a run passes through.
- `src/Maf.Lab.Api/Agent/Streaming/` — a recorder; `Storage/MafDbContext.cs` — a row for the frames.
- `src/Maf.Lab.Api/Endpoints/TraceEndpoints.cs`, `Agent/Tracing/TraceRetentionService.cs` — serve and purge them.
- `docs/trace-events.md`, `docs/http-api.md` — the new shape.
- No change to what is streamed, so no change to the protocol or to any other consumer.

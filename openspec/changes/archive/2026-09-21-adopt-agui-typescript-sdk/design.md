# Design

## Context

See proposal.md for motivation. The current pipeline:

```
SSE bytes → SseParser → SseFrame{event:string, data:string}
         → toChatEvents(frame) → ChatStreamEvent[]   (web/src/chat/chatEvents.ts)
         → dispatch({type:'event', event})            (chatReducer.ts)
```

`toChatEvents` switches on `frame.event` bare strings and extracts fields from
`JSON.parse(frame.data)` cast to `Record<string,unknown>`. If the server adds a new
event type, the TypeScript compiler does not catch the missing branch; it silently
falls through to `return []`.

`@ag-ui/core` 1.0.0 provides:
- `EventType` enum — every discriminator as a typed constant
- Concrete event classes — `TextMessageContentEvent`, `ToolCallStartEvent`, etc.
- `BaseEvent` — the open union with an index signature for extensibility

`@ag-ui/encoder` 1.0.0 has an encoder only (no SSE decoder), so `SseParser` stays.

## Goals / Non-Goals

**Goals:**
- `EventType.*` replaces every bare string in the discrimination switch
- The input to `toChatEvents` (or its replacement) is a typed `BaseEvent`, not a `SseFrame` with unknown payload
- `ChatStreamEvent` re-expressed using SDK event field types so `event.type` checks are compile-safe
- Test helpers (`run.*`) use `EventType.*` constants
- `SseParser` is unchanged except that its public output is consumed as `BaseEvent`, making `SseFrame` internal

**Non-Goals:**
- Replacing `SseParser` with any third-party SSE decoder (none exists in the SDK; hand-rolled parser is correct)
- Adopting `@ag-ui/client` (brings rxjs; not warranted for a reducer-based consumer)
- Changing `chatReducer`, `useChatStream`, the monitor, or time travel behaviour in any way

## Decisions

### 1. Keep the `ChatStreamEvent` / `chatReducer` boundary; make it typed

**Decision**: `ChatStreamEvent` in `api/types.ts` is re-expressed with SDK field types instead of
`{ type: 'text_delta'; data: { text: string } }` etc. The internal names (`text_delta`, `done`,
`sources`, `trace`, `confirmation_required`) remain — they are the app's own vocabulary — but the
types of their `data` payloads now reference `@ag-ui/core` event field shapes where they align
(e.g. `TextMessageContentEvent` carries `delta: string`, so `text_delta.data.text` reads
`event.delta` instead of `data['delta']`).

**Alternative considered — eliminate `ChatStreamEvent`, make reducer handle `BaseEvent` directly**: 
Rejected. `chatReducer` is a tested state machine with a stable shape. Making it understand every
AG-UI event type (including ones it does not render) couples the app to the full protocol surface.

### 2. `toChatEvents` receives `BaseEvent`, not `SseFrame`

**Decision**: `readChatStream` parses each SSE frame's `data` field with `JSON.parse` and casts to
`BaseEvent` **before** passing to `toChatEvents`. `toChatEvents` signature becomes
`(event: BaseEvent) => ChatStreamEvent[]`. The `SseFrame` interface is marked `/** @internal */`
and not exported.

`frame.event` (the SSE event-name line) is no longer used for discrimination — the event's own
`type` field (now typed as `EventType`) is used instead. This aligns with how the `.NET` side works:
`AGUIStream.FrameName(e)` copies `e.Type` into the frame name, so they are always equal; the
`type` field on the JSON payload is canonical.

### 3. `EventType.*` constants replace all bare string literals

**Decision**: Every `case 'TEXT_MESSAGE_CONTENT':` becomes `case EventType.TEXT_MESSAGE_CONTENT:`.
TypeScript exhaustiveness checking (or an explicit `default: return []` that is a compile error if
all reachable branches are covered) makes a missed event type visible at compile time.

### 4. Test helpers use `EventType.*`

**Decision**: `render.tsx` `run` helper functions construct SSE frames with
`sse(EventType.TEXT_MESSAGE_CONTENT, ...)` etc. The frame format (SSE `event:` line) is unchanged;
only the string constant source moves.

### 5. DECISIONS.md records the reversal

A new entry in DECISIONS.md notes that `@ag-ui/core` 1.0.0 reached zero-dependency stable release,
making the original trade-off moot, and pins the version.

## Risks / Trade-offs

- **`@ag-ui/core` version drift**: The package is pinned in `package.json` to `1.0.0` (exact).
  The project's convention (DECISIONS.md on every version move) governs any future bump.
  Risk: low — the package has no runtime deps and a stable 1.0 schema.
- **`BaseEvent` index signature**: `@ag-ui/core` `BaseEvent` carries `[key: string]: unknown`,
  which keeps unknown fields accessible without type errors. This is intentional (the compat layer
  documents it) and does not affect the project.

## Migration Plan

Frontend-only change. No deploy coordination required. All existing tests pass after the refactor —
`readChatStream` and `chatReducer` behaviour is unchanged; only types and constants are updated.

# Proposal

## Why

DECISIONS.md §26 explicitly records a trade-off: the web client hand-rolls its own AG-UI event parsing
rather than using the official TypeScript SDK, because at the time "the protocol's TypeScript packages
would be a second dependency for a client that renders six kinds of event." That reason no longer holds:
`@ag-ui/core` 1.0.0 was published 2026-09-17 with **zero runtime dependencies**, typed event classes,
and the `EventType` enum that makes the protocol's discriminators compile-safe. The hand-rolled approach
in `chatEvents.ts` — switching on raw strings like `'TEXT_MESSAGE_CONTENT'` — is the only remaining
place in the project where the AG-UI abstraction is not used as-is.

## What Changes

- **Add `@ag-ui/core` to `web/package.json`** as a production dependency (pure types + compiled validators,
  no transitive deps).
- **Replace `web/src/chat/chatEvents.ts`** (`toChatEvents`): the hand-rolled string switch is deleted.
  The new translation uses `EventType.*` constants from `@ag-ui/core` and reads typed fields directly
  from the parsed `BaseEvent` objects rather than from `Record<string,unknown>`.
- **Narrow `SseFrame` output to typed `BaseEvent`**: `SseParser.push` / `flush` continue to parse
  the WHATWG SSE wire format (no decoder exists in the official package yet), but their frames are
  immediately passed through `JSON.parse` and typed as `BaseEvent` before `toChatEvents` sees them.
  The `SseFrame` interface becomes internal to the parser.
- **Replace `ChatStreamEvent` in `web/src/api/types.ts`** with a type derived from `@ag-ui/core`:
  the internal discriminated union is re-expressed using the SDK's concrete event types
  (`TextMessageContentEvent`, `ToolCallStartEvent`, `ToolCallResultEvent`, `RunFinishedEvent`,
  `RunErrorEvent`, `CustomEvent`) so the reducer's `event.type` checks compile against `EventType`.
- **Update `web/src/test/render.tsx`** test helpers: `run.*` factory functions replace bare string
  event names (`'TEXT_MESSAGE_CONTENT'` etc.) with `EventType.*` constants from `@ag-ui/core`.
- **Update DECISIONS.md** to record the reversal of the "no SDK" decision and pin the package version.

## Capabilities

### New Capabilities

*(none — this is a library adoption and internal refactor)*

### Modified Capabilities

- `agui-stream`: The requirement "What the protocol does not name travels as a custom event" currently
  says sources and trace SHALL travel as custom events under stable names. That is unchanged. A new
  scenario is added: the web client SHALL discriminate protocol events by the protocol's own type
  constants (`EventType.*`) rather than by hand-rolled string literals, so a future event added to the
  SDK is a compile error at the discrimination site rather than a silent fall-through.

## Impact

- `web/package.json` — new dependency `@ag-ui/core`
- `web/src/chat/chatEvents.ts` — rewritten (type discrimination only; `SseFrame` shape shrinks to internal)
- `web/src/chat/sseParser.ts` — minor: `SseFrame` marked internal; output consumed as `BaseEvent`
- `web/src/chat/readChatStream.ts` — minor: callback type changes from `ChatStreamEvent` to the new type
- `web/src/api/types.ts` — `ChatStreamEvent` replaced; `ConfirmationRequiredData`/`DoneData` aligned
- `web/src/chat/chatReducer.ts` — action `ChatAction.event` payload type updated
- `web/src/test/render.tsx` — `run.*` helpers use `EventType.*` constants
- All web tests that reference `ChatStreamEvent` or the old string literals require corresponding updates
- No backend changes; no API contract changes; no eval impact; no service changes
- DECISIONS.md — new entry reversing §26's "no SDK" decision

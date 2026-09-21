# Tasks

## 1. Install the SDK

- [ ] 1.1 In `web/package.json`, add `"@ag-ui/core": "1.0.0"` to `dependencies`.
  Run `npm install` to update `package-lock.json` and verify the package appears in
  `node_modules/@ag-ui/core`. Verify: `cd web && node -e "require('@ag-ui/core')"` exits 0.

## 2. Update `SseParser` — make `SseFrame` internal

- [ ] 2.1 In `web/src/chat/sseParser.ts`, mark `SseFrame` as `/** @internal */` and stop
  re-exporting it from the module's public surface (or prefix it with an underscore, whichever
  the codebase convention favours). No logic change — the parser itself is unchanged.
  Verify: `make lint-web` passes.

## 3. Retype the event pipeline — `BaseEvent` flows from parser to translator

- [ ] 3.1 In `web/src/chat/readChatStream.ts`, change the signature of `toChatEvents` usage:
  after each `SseFrame` is produced, parse its `data` field with `JSON.parse` and cast to
  `import type { BaseEvent } from '@ag-ui/core'`. Pass the resulting `BaseEvent` (not the
  `SseFrame`) to `toChatEvents`. The `SseParser` is not changed. The callback type stays
  `(event: ChatStreamEvent) => void`.
  Verify: `cd web && npm run build` compiles with no errors.

## 4. Rewrite `chatEvents.ts` — `EventType.*` for all branches

- [ ] 4.1 In `web/src/chat/chatEvents.ts`, change `toChatEvents` to accept `BaseEvent` (from
  `@ag-ui/core`) instead of `SseFrame`. Replace every `case 'TEXT_MESSAGE_CONTENT':` etc. with
  `case EventType.TEXT_MESSAGE_CONTENT:` from `@ag-ui/core`. Read typed fields from the concrete
  event type rather than from `data['field']` casts.
  - `EventType.TEXT_MESSAGE_CONTENT` → `(event as TextMessageContentEvent).delta`
  - `EventType.TOOL_CALL_START` → `(event as ToolCallStartEvent).toolCallId`, `.toolCallName`
  - `EventType.TOOL_CALL_ARGS` → `(event as ToolCallArgsEvent).toolCallId`, `.delta`
  - `EventType.TOOL_CALL_RESULT` → `(event as ToolCallResultEvent).toolCallId`, `.messageId`, `.content`
  - `EventType.CUSTOM` → `(event as CustomEvent).name`, `.value`
  - `EventType.RUN_FINISHED` → `(event as RunFinishedEvent).outcome`, `.result`, `.threadId`
  - `EventType.RUN_ERROR` → `(event as RunErrorEvent).message`
  Verify: `cd web && npm run build` compiles; `npm test -- --run src/chat/chatEvents.test.ts` passes.

## 5. Update `api/types.ts` — `ChatStreamEvent` re-expressed with SDK types

- [ ] 5.1 Remove the import of `SseFrame` from `chatEvents.ts` in `api/types.ts` (if present).
  Update the `ChatStreamEvent` type so that the `data` payloads align with the SDK field shapes.
  At minimum, the `{ type: 'text_delta'; data: { text: string } }` variant's `text` is now read
  from `TextMessageContentEvent.delta` in `chatEvents.ts` (the `ChatStreamEvent` shape itself
  can remain for the reducer — it is the internal representation).
  Verify: `cd web && npm run build` with no type errors.

## 6. Update test helpers — `EventType.*` constants

- [ ] 6.1 In `web/src/test/render.tsx`, replace every bare SSE event-name string with
  `EventType.*` from `@ag-ui/core`:
  - `sse('RUN_STARTED', ...)` → `sse(EventType.RUN_STARTED, ...)`
  - `sse('TEXT_MESSAGE_START', ...)` → `sse(EventType.TEXT_MESSAGE_START, ...)`
  - `sse('TEXT_MESSAGE_CONTENT', ...)` → `sse(EventType.TEXT_MESSAGE_CONTENT, ...)`
  - `sse('TEXT_MESSAGE_END', ...)` → `sse(EventType.TEXT_MESSAGE_END, ...)`
  - `sse('TOOL_CALL_START', ...)` → `sse(EventType.TOOL_CALL_START, ...)`
  - `sse('TOOL_CALL_ARGS', ...)` → `sse(EventType.TOOL_CALL_ARGS, ...)`
  - `sse('TOOL_CALL_END', ...)` → `sse(EventType.TOOL_CALL_END, ...)`
  - `sse('TOOL_CALL_RESULT', ...)` → `sse(EventType.TOOL_CALL_RESULT, ...)`
  - `sse('CUSTOM', ...)` → `sse(EventType.CUSTOM, ...)`
  - `sse('RUN_FINISHED', ...)` → `sse(EventType.RUN_FINISHED, ...)`
  - `sse('RUN_ERROR', ...)` → `sse(EventType.RUN_ERROR, ...)`
  Verify: `cd web && npm run build` compiles.

## 7. Regression pass

- [ ] 7.1 Run the full web test suite: `make test-web`. All 186 tests must pass.

- [ ] 7.2 Run `make lint-web`. No lint or type errors.

- [ ] 7.3 Run `make build-web`. Production build must succeed.

## 8. Update DECISIONS.md

- [ ] 8.1 Add a new entry to `DECISIONS.md` recording the reversal of the §26 "no SDK" decision:
  `@ag-ui/core` 1.0.0 reached zero-dependency stable release on 2026-09-17; the original
  trade-off ("a second dependency for a client that renders six kinds of event") no longer
  applies. Pin: `@ag-ui/core@1.0.0` in `web/package.json`.
  Verify: `DECISIONS.md` is committed alongside the code changes.

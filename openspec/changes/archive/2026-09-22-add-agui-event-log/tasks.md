# Tasks

## 1. The frame on the wire, on the client

- [x] 1.1 Add the `AguiFrame` shape (`seq`, `atMs`, `type`, `name?`, `bytes`, `traceSeq?`, `payload?`, `unparsed?`)
      and the `{ type: 'agui_frame' }` variant to `ChatStreamEvent` in `web/src/api/types.ts`; verify `npm run -w web build`
      type-checks.
- [x] 1.2 Give `readChatStream` an `onFrame` callback called once per SSE frame before its mapped events, numbering
      frames from 1, timing them from the run's first frame, reading `name`/`traceSeq` from a custom frame and
      dropping a trace frame's payload, and recording an unparseable frame's raw text; verify new cases in
      `web/src/chat/readChatStream.test.ts` cover a handled type, an unhandled type (`TEXT_MESSAGE_START`), a trace
      custom frame and malformed JSON.
- [x] 1.3 Dispatch the frames from `useChatStream` (both `send` and `answer`) as `agui_frame` chat events; verify the
      resume path is covered by `ChatPage.confirmation.test.tsx` still passing and a new assertion that the answer
      run's frames reach state.

## 2. Frames in the chat state

- [x] 2.1 Extend `traceReducer` state with `framesByKey`, handled by an `appendFrame` action and cleared by `reset`,
      reusing the existing key/`attach` bookkeeping; add `framesFor(state, keyOrTurnId)`; verify new cases in
      `web/src/monitor/traceReducer.test.ts` including lookup by the server turn id after `attach`.
- [x] 2.2 Route `agui_frame` through `chatReducer` into that state; verify `web/src/chat/chatReducer.test.ts` shows
      frames landing on the streaming assistant turn and not leaking into another turn.
- [x] 2.3 Extend `evals/ui-events.jsonl` expectations with a frame count per recorded run and assert it in
      `recordedRuns.test.ts`; verify the replayed runs report exactly the frames they carry.

## 3. The AG-UI tab

- [x] 3.1 Add an `AguiTab` to `web/src/monitor/MonitorTabs.tsx` rendering one row per frame — position, `+atMs`,
      event type, custom name, size, expandable payload via `JsonView`, the raw text for an unparsed frame, and a
      trace row that names the trace event and its seq instead of its data; verify a new `MonitorTabs`/`AguiTab` test
      asserts one row per frame with no coalescing.
- [x] 3.2 Register the `AG-UI` tab in `MonitorPanel`'s `TABS` and pass it the selected turn's frames, with the
      "not recorded" message for a turn that has none; verify `MonitorPanel.test.tsx` covers both states.
- [x] 3.3 Apply the cursor to the tab: frames up to and including the one whose `traceSeq` is the cursor's trace
      event are reached, that one is highlighted, later ones are dimmed, and cursor 0 reaches none; verify a new
      case in `TimeTravel.ui.test.tsx` steps the cursor and asserts the dimming.
- [x] 3.4 Style the rows in `MonitorPanel.module.css` reusing the timeline's row, chip, future and current classes;
      verify the tab reads correctly at the panel's narrow width alongside the other tabs.

## 4. Recording the frames on the server

- [x] 4.1 Add a `RunFrameRecorder` under `src/Maf.Lab.Api/Agent/Streaming/` that takes each `BaseEvent`, produces the
      frame record (a trace custom event by name + trace seq + size, no payload copy), caps the run at 256 KB and
      marks frames past the cap as truncated; verify a new xUnit test covers ordering, the trace-by-reference rule
      and the cap.
- [x] 4.2 Record in `ChatEndpoints.Stream` as each event is yielded, including the cancellation path's terminal
      event; verify a test asserts the recorded list matches the events the stream produced, terminal event included.
- [x] 4.3 Persist the frames after the stream ends, keyed by the turn id read from `RunFinishedEvent`, writing
      nothing when there is no turn id; verify tests cover a normal turn (frames stored) and a resume run (nothing
      stored).

## 5. Keeping and serving the frames

- [x] 5.1 Add the nullable `AguiJson` column to `TurnTraceRow`; verify `DatabaseInitializer`'s additive pass adds it
      to an existing database in its test.
- [x] 5.2 Return the frames from `GET /api/turns/{turnId}/trace` as `aguiFrames`, `null` when none were recorded;
      verify endpoint tests cover a turn with frames, a turn without, and that the owner/FIRM_ADMIN/other-firm rules
      are unchanged, with the other firm seeing not found.
- [x] 5.3 Confirm retention deletes the frames with the trace and that no log line carries a frame payload; verify a
      test purges an old trace and asserts its frames are gone.
- [x] 5.4 Read `aguiFrames` in `useTurnTrace`/`api/types.ts` and prefer the live frames for the selected turn,
      falling back to the stored ones; verify a `ChatPage` test shows the stored frames after reopening a turn.

## 6. Documentation and verification

- [x] 6.1 Document the frame record and the `aguiFrames` field in `docs/trace-events.md` and `docs/http-api.md`;
      verify the documented shape matches what the endpoint returns.
- [x] 6.2 Run `make lint` and `make test` (web + .NET) and confirm they pass — and `make verify`
      against the rebuilt stack, which is what that target actually checks.
- [x] 6.3 Run the stack (`make`), ask a question, open the AG-UI tab, and confirm every frame of the run is listed
      live; reload the conversation and confirm the same list comes from the stored turn.

# Tasks

## 1. Answer text in the trace (API)

- [x] 1.1 Coalesce text deltas into `answer.delta { offset, text }` trace events in `ChatTurnRunner` (flush at 160 chars, 150 ms, before a tool call, at end); update `docs/trace-events.md`; verify a test that offsets are contiguous from 0, chunks concatenate to the streamed answer, and a long answer yields far fewer chunks than deltas

## 2. Time-travel model (web)

- [x] 2.1 `timeTravelReducer` + `effectiveCursor` (live/following, seek, step, start/end, play/pause/tick, speed, compress, goLive, eventsChanged); verify reducer unit tests incl. leaving and returning to live while events arrive
- [x] 2.2 `usePlayback` scheduling by recorded `atMs` gaps / speed with compressed waits and stop at end; verify with fake timers (10× replay timing, compress cap, pause)
- [x] 2.3 `reconstructTurn(events, cursor)` (answer text, tool card states, sources, fallback for traces without `answer.delta`); verify unit tests for each scenario

## 3. UI

- [x] 3.1 `TimeTravelBar` (scrubber with aria-valuetext, step/jump buttons, play/pause, speed select, compress toggle, "Back to live", step k/N) and scoped keyboard shortcuts; verify rendering and interaction tests incl. keys not firing from the chat input
- [x] 3.2 Monitor tabs render `visible` events, Timeline dims future rows and seeks on click, Model tab shows "waiting for response…", and a "this step" panel; verify tests for retrieval before/after and model-in-progress scenarios
- [x] 3.3 Chat reconstruction for the selected turn with the "viewing step k of N" banner and return; verify tests for rewound answer text and running tool card, other turns unaffected

## 4. Verification and docs

- [x] 4.1 In the running stack (balancer on :7171): ask a question, scrub back to the forced search, step through retrieval and model calls, replay at 10×, reopen a stored turn and rewind; screenshot. Run `make test`, `make lint`, `make ci-e2e`
- [x] 4.2 Update README (time travel in the monitor section), DECISIONS.md (answer.delta coalescing, time-travel model); verify sections exist

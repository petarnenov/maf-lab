# Tasks

This change is retroactive: the work is in `main` (`a798799`, `0771f35`). Each task is therefore a check that
what was built matches what the specs now say — and each one names where the code and its proof already are, so a
task that does not hold is a task that changes the code rather than the file it points at.

## 1. The monitor is a panel

- [x] 1.1 Pressing the control on the turn the monitor is showing closes it, and pressing it again opens it on
  that turn; the conversation takes the room while it is closed — verify `ChatPage.test.tsx` "the button closes
  the monitor and opens it again", and that `ChatPage.module.css` narrows the grid when the monitor is closed
- [x] 1.2 Nothing but that control closes the monitor: clicking the body of the turn being shown only shows it —
  verify `ChatPage.test.tsx` "clicking the bubble shows its trace but never closes the monitor"
- [x] 1.3 The rest of the screen is unchanged — verify the existing layout, past-turn selection and time-travel
  tests still pass (`make test-web`)

## 2. A recording is replayed at its own moment

- [x] 2.1 Every row of `evals/ui-events.jsonl` carries when it was captured, and `scripts/capture_ui_events.sh`
  writes it — verify `recordedRuns.test.ts` asserts each row's `capturedAt` parses
- [x] 2.2 Replaying a row sets the clock to that moment, so a recording whose proposal has since expired still
  reaches the state it recorded — verify `recordedRuns.test.ts` "pauses-for-confirmation" passes with the
  recording older than a proposal's lifetime

## 3. The diagram shows what it draws

- [x] 3.1 No two boxes in `docs/topology.drawio` overlap — verify `TopologyTests.No_two_boxes_in_the_diagram_overlap`
- [x] 3.2 An edge stops at the boxes it joins, and one that would pass through a third box goes around it, above
  the row when that lane is clear and below it otherwise — verify the four cases in `web/src/topology/layout.test.ts`
- [x] 3.3 A redrawn diagram reaches the browser rather than being answered from its own cache — verify
  `TopologyTests.The_diagram_is_served_and_holds_exactly_the_reported_nodes` asserts `no-cache`

## 4. Verification and the record

- [x] 4.1 Live: open `/chat`, close and reopen the monitor from the button, then open `/topology` and confirm the
  compliance box shows its health and replicas and the `index admin` line is visible along its whole length
  (the conversation went 685 → 1524 px wide with the panel closed and back; clicking the answer left it open;
  compliance rendered "● healthy 2/2 up"; no two boxes overlapped; the `index admin` line ran 550,290 → 550,250
  → 1000,250 → 1000,290, with no point inside any box)
- [x] 4.2 Run `make lint`, `make test`, `make verify` against the running stack (lint clean; 409 .NET and 185 web
  tests pass; all verify checks pass. One .NET test failed on the first run and passed on the next two, with a
  `Qdrant bootstrap attempt 1 failed: RpcException` in the captured log — a container-start race in the
  integration tests, unrelated to this change and not introduced by it; recorded here rather than dismissed)
- [x] 4.3 `DECISIONS.md` records why the process was skipped and what it cost, so the next change does not repeat
  it — verify the section exists and names both commits

# Tasks

## 1. Web

- [x] 1.1 In `useTimeTravel`, reset on a new turn and take in a new event count during the render instead of in
  effects. Verify with `useTimeTravel` tests: a following cursor equals the event count on the very render the events
  grow; a pinned cursor on one turn is not applied to the next turn's first render.
- [x] 1.2 In `ChatPage`, rewind the selected turn only when the cursor was moved. Verify with a `ChatPage` test: while
  a turn streams reasoning with the monitor open, no render shows the rewind banner.

## 2. Verification

- [x] 2.1 Run `make test`, `make lint` and `make build-web`; all green.
- [x] 2.2 Rebuild with `make`, ask a question that reasons at http://localhost:7171/chat with the monitor open: no
  banner and no flash while it streams; clicking a step in the timeline shows the banner, "Return to now" removes it.
- [x] 2.3 Run `openspec validate fix-rewind-banner-flash --strict`; valid.

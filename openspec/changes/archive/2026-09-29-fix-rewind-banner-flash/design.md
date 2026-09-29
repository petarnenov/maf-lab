# Design

## Context

`useTimeTravel` keeps the cursor in a reducer. A following cursor (`'live'`) resolves to the reducer's `count`, and
`count` was set from `events.length` in a `useEffect`. Effects run after the commit, so each render that received a new
event committed with `count` one behind. `ChatPage` treated any `cursor < events.length` as a rewound turn.

## Decisions

**1. Catch up during the render.** The hook stores the turn it was last reset for. When the turn or the event count
differs, the render computes the caught-up state locally and dispatches the same action. React re-runs the render
before committing, and even the discarded pass resolves against the right state. The two effects are gone.
*Alternative:* resolve `'live'` against `events.length` at read time only. That fixes the event lag but not a cursor
pinned on the previous turn after a turn switch.

**2. Only a moved cursor rewinds the chat.** `ChatPage` also requires `state.cursor !== 'live'`. Following the newest
step is, by definition, "now". The banner then depends on what the user did, not on how the timing of two values lines
up. A cursor the user moves to the last step is numeric but equal to the count, so it does not rewind either.

## Risks

- A dispatch during the render must be conditional, or it loops. Both are guarded by an inequality that the dispatch
  itself removes.

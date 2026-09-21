# Proposal

## Why

Three things the person in front of the screen could not see, and one process failure.

The behind-the-scenes monitor followed the newest turn through an implicit fallback — no selection meant "the
latest one" — so the newest bubble's button always read "Showing behind the scenes" and pressing it assigned the
state that was already in effect. The button was inert in the one direction a person tries first, while its label
promised a toggle. The topology picture had the same shape of problem twice: the compliance box was drawn over
the chat provider, hiding its health and replica count, and an edge between two boxes with a third between them
was drawn straight through it, so the api's "index admin" line to qdrant was invisible. A redrawn diagram did not
even reach the browser, which was free to decide for itself how long the old one stayed fresh.

The process failure is the reason this proposal is retroactive: the work is already in `main` (`a798799`,
`0771f35`). It was written, tested and pushed without a change, which left the specs describing a system that no
longer matches — the thing the process exists to prevent. This change records what was built so the specs are
true again; its tasks are verification, not construction.

## What Changes

- **The monitor is a panel a person opens and closes.** Pressing the button on the turn being shown closes it and
  the conversation takes the room; pressing it again opens it on that turn. Clicking the bubble itself only ever
  shows a turn — closing the panel by clicking the answer you are reading would be a surprise.
- **A recorded run is replayed at its own moment.** Rows in `evals/ui-events.jsonl` carry real timestamps — a
  proposal's expiry above all — so each row now records when it was captured and the replay sets its clock to
  that. Without it the recordings rot: a run recorded as `waiting` reads `expired` an hour later.
- **The diagram shows what it draws.** No two boxes overlap. An edge that would pass through a third box goes
  around it rather than under it. And the diagram is served so that a redrawn one reaches the browser rather than
  being answered from its cache without asking.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `web-ui`: the behind-the-scenes monitor is a panel that opens and closes, and which turn it shows is decided by
  an explicit choice rather than by falling back to the newest one.
- `system-topology`: the drawing must be legible — nothing hidden under anything else — and the served diagram
  must be the one currently drawn.
- `eval-harness`: a recorded run carries when it was captured, and is replayed at that moment.

## Impact

- **Changed**: `web/src/chat/ChatPage.tsx` and its stylesheet; `web/src/topology/TopologyDiagram.tsx`;
  `docs/topology.drawio`; `src/Maf.Lab.Api/Endpoints/TopologyEndpoints.cs`; `scripts/capture_ui_events.sh` and
  `evals/ui-events.jsonl`.
- **New**: `web/src/topology/layout.ts` — where an edge runs between two boxes.
- **Dependencies**: none.
- **Risk**: the monitor can now be closed, so a reader arriving at a screenshot of a closed panel may think it is
  gone. It reopens from the same button, which is where they will look.

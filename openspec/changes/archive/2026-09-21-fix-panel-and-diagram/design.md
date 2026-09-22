# Design

## Context

See proposal.md — Why. What exists and shapes this:

- **The monitor's selection had no closed state.** `ChatPage` held `selectedKey: string | null` and resolved it
  with `find(selectedKey) ?? latest`, so `null` meant "the newest turn". Every state was a turn; there was no
  value that meant "nothing".
- **Edges were straight lines between centres.** `TopologyDiagram` drew one `<line>` per edge from the centre of
  one box to the centre of the other. Nothing consulted the other boxes, and the drawing file's own
  `edgeStyle=orthogonalEdgeStyle` was read and discarded by the parser as a style.
- **The diagram is a file on disk served by the api.** `GET /api/topology/diagram` returns `docs/topology.drawio`
  through `Results.File`, which sends an entity tag and a last-modified date and no cache directive.
- **The recorded runs are new.** `evals/ui-events.jsonl` was added the day before by `add-a2a-admin-evals`; its
  rows held frames and an expected state and nothing about when they were taken.

## Goals / Non-Goals

**Goals:**

- Nothing in either picture hidden behind anything else.
- A control whose label and its effect agree.
- Recordings that stay true as they age.

**Non-Goals:**

- No diagram editor, and no automatic layout. The drawing stays hand-placed in draw.io; only the lines between
  the boxes are computed.
- No general orthogonal router. The detour handles the case the drawing has — two boxes in a row with a third
  between them — and falls back to a straight line rather than growing a pathfinder.
- No change to what the monitor shows or to time travel.

## Decisions

### The monitor keeps "follow the latest" and gains an open flag

The alternative was to drop the implicit fallback and make the selection always explicit, setting it to each new
turn as it arrives. That is one state instead of two, but it needs an effect that watches the turn list and
writes state, and it changes what a fresh conversation shows before the first turn exists. Keeping the fallback
and adding `monitorOpen` leaves the existing behaviour alone and adds exactly the state that was missing: the
panel being closed is not a property of any turn, so it was never going to be expressible as a turn id.

The bubble keeps a click that only shows. A control that closes and a surface that closes are two ways to lose
the panel, and the surface is the answer text a person is reading.

### An edge is clipped to the borders it joins, and goes around what is in its way

Three routes were considered. A full orthogonal router with obstacle avoidance is the general answer and far more
code than one drawing needs. Waypoints in the `.drawio` file would move the decision to the person drawing, which
is the right place in draw.io but means the app renders nothing useful until someone opens the editor. What was
built is the middle: a straight line clipped to both boxes' borders, and — only when that line would pass through
a third box — a three-segment detour through a clear lane, tried above the row first and below it second, with a
straight line as the last resort so an unroutable edge is still drawn.

Clipping to the border is worth having on its own: a centre-to-centre line starts under the source's own title.

### Overlap is a test, not a renderer's problem

A renderer could nudge overlapping boxes apart, and then the picture on screen would not be the picture in the
file — which the existing requirement says it must be. So the drawing stays authoritative and the test refuses a
file whose boxes overlap, the same way it already refuses one whose nodes do not match the report.

### The diagram is served `no-cache`, not `no-store`

`no-store` would forbid keeping it at all and re-transfer the file on every visit. `no-cache` keeps the entity
tag doing its work — a browser that already has the current drawing gets a 304 — and only forbids answering from
the copy without asking. The failure being fixed is the heuristic one: with no directive at all, freshness is
guessed from the file's age, so a diagram edited weeks ago is treated as fresh for days.

### A recording carries its own moment

The clock could have been frozen at one arbitrary date for the whole suite, but then a new recording would have
to be made to agree with a date that has nothing to do with it. Recording `capturedAt` per row keeps each row
self-contained: it says when it was true, and the replay believes it.

## Risks / Trade-offs

- **The detour lane can be occupied by an edge rather than a box** → Lines crossing lines is ordinary in a
  diagram; lines hidden under boxes is not. Only boxes are treated as obstacles.
- **A closed monitor persists for the session** → Deliberate: re-opening it on the next message would undo a
  choice the person just made. It is one button away.
- **`no-cache` costs a revalidation round trip per view** → A conditional request against a local file, on a
  screen nobody opens in a loop.
- **This change is retroactive** → Its tasks verify rather than build, which is weaker than the usual order: the
  specs were written knowing what the code does, so they cannot have caught a disagreement. The mitigation is
  honesty about it here, and not repeating it.

## Migration Plan

None. No data, no configuration, no stored state.

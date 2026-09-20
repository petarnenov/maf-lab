# Proposal

## Why

Two agents talk to this system and this system talks to a third, and none of it is visible anywhere. A partner's
task runs, retries, gets cancelled or times out; a compliance review is consulted and answers or does not; a push
webhook is delivered or fails three times — all of it recorded, none of it shown. The only way to see what A2A
did is to read the audit table by hand.

The conformance story has the same shape. `tools/Maf.Lab.A2AProbe` proves an outside client can drive both agents
from nothing but their cards, which is the strongest claim this project makes about A2A — and it makes that claim
as a hard-coded list inside a console program that nothing runs automatically. The scenarios it checks are
exactly the ones the Day-4 brief asked to be a dataset.

And the two things that were deferred every time they came up: a verdict carrying an embedded instruction, which
has a unit test for the check but no fixture set behind it; and the browser's reducer, which is tested against
hand-written events rather than against runs the server actually produced.

## What Changes

- **An `/admin/a2a` screen.** What arrived from partners, what this system asked of the reviewer, and every push
  delivery — each with its state, when it happened and how long it took. A task that is still running can be
  cancelled from there.
- The screen is a firm admin's, and what it shows is their firm's. The audit record already knows which firm each
  A2A request concerned, so that is what scopes it rather than a new column and a new rule.
- **The conformance scenarios become a dataset.** `evals/a2a-conformance.jsonl` lists what an outside client must
  be able to do — discovery, the extended card after authentication, a direct answer, a streamed task, resuming
  after a dropped stream, resuming a question, cancelling, a push delivery, and a request outside a partner's
  firms being refused. The probe reads it and runs them, and writes a report in the same shape every other suite
  writes, so a failure is visible the way a failing eval is.
- **Verdicts with embedded instructions become a fixture set.** `evals/injection-a2a.jsonl` holds what a hostile
  or broken reviewer might send; each one is driven through the verdict check and the write flow, and must change
  nothing about what executes.
- **Recorded runs become the reducer's test data.** `evals/ui-events.jsonl` holds AG-UI runs captured from the
  running stack, each with the state the browser should end in. Replaying them is how the reducer is proved
  against what the server actually emits rather than against what a test author imagined.

Not in this change: nothing. This is the last of the Day-4 split.

## Capabilities

### New Capabilities

- `a2a-observability`: what an operator can see of the conversations between agents, and what they can stop.

### Modified Capabilities

- `eval-harness`: two more datasets and a conformance report that is not produced by the harness itself.
- `a2a-hosting`: an inbound task can be cancelled by the firm it concerns, not only by the partner that started it.
- `web-ui`: the A2A screen, and the reducer proved against recorded runs.

## Impact

- **New**: `/api/admin/a2a` and its cancel endpoint; `web/src/admin/A2AAdminPage.tsx`; `evals/a2a-conformance.jsonl`,
  `evals/injection-a2a.jsonl`, `evals/ui-events.jsonl`; a conformance runner in the probe; fixture-driven tests for
  the verdicts and the reducer.
- **Changed**: `tools/Maf.Lab.A2AProbe` reads its scenarios instead of holding them; the Makefile and the CI
  end-to-end job run the conformance set; `docs/http-api.md`, README, `DECISIONS.md`.
- **Dependencies**: none.
- **Risk**: the probe deliberately shares no code with the service — that is what makes it evidence. Reading a
  dataset keeps that property; importing anything from `src/` would lose it, so the runner stays inside the probe
  even though a suite in the eval harness would have been less code.

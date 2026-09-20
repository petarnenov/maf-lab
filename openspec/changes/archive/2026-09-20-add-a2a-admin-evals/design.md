# Design

## Context

See proposal.md — Why. What exists and shapes this:

- **Everything is already recorded, nothing is shown.** `A2ATaskRow` holds each inbound task with its partner,
  state and times; `A2APushDeliveryRow` holds every delivery attempt with its state, attempts, outcome and error;
  the audit chain holds one `a2a.request` row per inbound request and one `a2a.consultation` row per outbound
  one, each carrying `taskId=…` and the firm.
- **The task table has no firm.** `A2ATaskRow` is this project's implementation of the SDK's `ITaskStore`, keyed
  by task and context. The audit rows do have a firm — the inbound handler files each request under the partner's
  allowed firm — but only once the request has finished.
- **Cancelling exists, for partners.** `A2ARequestHandlerWithExtras` serves `CancelTask` to an authenticated
  partner. Nothing lets the firm whose data is being worked on stop it.
- **The probe is the outside client.** `tools/Maf.Lab.A2AProbe` drives both agents from their cards, with a
  hard-coded list of checks and a `Check(ok, what)` helper that prints and counts failures. It references only
  `A2A` and `Microsoft.Agents.AI` — no project reference to `src/`.
- **The eval harness links against the service.** `Maf.Lab.Eval` references `Maf.Lab.Api`, which is why it can
  drive turns in-process. That is exactly what disqualifies it from proving what an outsider can do.
- **The reducer's tests are hand-written.** `chatReducer.test.ts` builds events by hand; `run.*` helpers in
  `test/render.tsx` write AG-UI frames. Nothing has ever replayed a run the server actually produced.

## Goals / Non-Goals

**Goals:**

- One screen that answers "what have the agents been doing", scoped the way everything else in this system is.
- The conformance claim kept honest: run by a client that shares no code, and run automatically.
- The two deferred fixture sets written down, so they can grow without touching code.

**Non-Goals:**

- No new A2A protocol surface. Cancelling already exists; this adds who may ask for it.
- No live updates on the screen. A refresh button is enough for a view of what already happened.
- No second tenancy rule. Whichever row carries the firm, it is stamped by the code that already resolves the
  partner's entitlement — nothing new decides who may see what.
- No conformance suite inside the eval harness, however much less code it would be.

## Decisions

### The screen is scoped by a firm stamped on the task, not by the audit

The first plan was to scope the screen by the audit: the audit record already carries the firm, so the task
table would not need a column. Building it disproved that. The audit row for an inbound request is written when
the request *finishes* — it records the outcome and the duration — so a task that is still running has no audit
row at all. The one question an operator opens this screen to ask is "what is running right now", and that
answer would have been the empty set.

So `A2ATaskRow` grows `FirmId` alongside the `PartnerId` it already meant to hold, stamped by `SqliteTaskStore`
when the task is created, from the same `IPartnerAccessor` that resolved the partner's entitlement for this
request. That is not a second tenancy rule: it is the same resolution, written down one row earlier. The screen
then lists the firm's tasks and joins the audit for the operation name and duration of the ones that finished.

Building it also showed `PartnerId` had never been populated — the store read a column nobody wrote — so the
stamp fixes an existing hole rather than adding a new mechanism.

### Cancelling from the screen goes through the same handler

The endpoint resolves the task the same way the list does — the task row's firm must be the caller's — and then
calls the same cancellation the partner would. It does not reach into the task store directly, so a cancelled task ends the
way a cancelled task ends, with the same events and the same final state.

### The conformance scenarios are executed by the probe, not by the harness

The strongest claim this project makes about A2A is that a client sharing no code can drive it. A suite inside
`Maf.Lab.Eval` would link against `Maf.Lab.Api` and prove nothing of the sort. So the dataset is read by the
probe, which keeps its independence, and the probe writes a report in the same shape the harness writes — a
suite name, a variant, a `passRate`, the failures by name — so a conformance failure reads like any other
failing eval and the CI job can treat it the same way.

The cost is that `make eval SUITE=…` does not run it: that target dispatches into the harness. `make eval-a2a`
runs the probe instead, and the end-to-end job runs it after `make verify`. Recorded here because a reader will
reasonably expect one command to run everything, and it does not.

### The hostile verdicts are a fixture set, not a model suite

"A verdict with an embedded instruction changes nothing about what executes" is a claim about code, not about a
model's judgement: the verdict is checked before it is believed, and the identifiers used afterwards are the ones
this system sent. A model suite could only show that an answer sounded right. So `injection-a2a.jsonl` is driven
by a data-driven test through the verdict check and the flow, which proves the claim deterministically — and
still lives in `evals/` because it is the same kind of artifact.

### The recorded runs are captured, not written

`ui-events.jsonl` rows are runs taken from the running stack — the frames as they came off the wire — each with
the state the reducer should reach. Writing them by hand would make them agree with the reducer by construction;
capturing them makes them agree with the server. Each row carries a name, the frames, and the expected answer,
tool calls, sources and pending write.

## Risks / Trade-offs

- **A task with no firm stamp is invisible** → Only tasks created before this change have none, and a row with no
  provenance is one nobody can be shown. New tasks are stamped as they are created.
- **Two ways to cancel** → Both end at the same handler, and the spec now says who may ask. A firm admin
  cancelling a partner's task is the point: it is their firm's data being worked on. The handler's cancel path
  had to stop requiring an authenticated partner, since an admin's cancel has none.
- **`make eval` does not run conformance** → Recorded above and in `DECISIONS.md`, with `make eval-a2a` and the
  CI job as the answer.
- **Recorded runs go stale when the protocol changes** → That is the value: a change in what the server emits
  fails the browser's tests, which is when someone should look. Re-capturing is a documented step.

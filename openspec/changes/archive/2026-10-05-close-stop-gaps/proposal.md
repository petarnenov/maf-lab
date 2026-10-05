# Proposal

## Why

Four gaps were left after `stop-everything`, looked at by what the rule is for — a person stops work that costs them
when they no longer want it:

1. The `web-ui` spec says mutations carry an abort signal; they do not, and they should not: the mutations here start
   work and answer at once with its id (an index run, a coverage refresh, a test-generation run that calls the model).
   Aborting one half-way could leave paid work running that the page never learned the id of, so could never stop.
2. Esc does not stop a loading read on the telemetry, Jev statistics, evals and A2A admin screens. These read
   Prometheus, stored traces, report files and the database — nothing paid — but "everything can be stopped" is the
   rule, and the fix is one hook per page.
3. `GraphStop` is what makes a stopped graph query stop in Neo4j (the driver alone runs it to the end, ~40 s). Nothing
   fails if a graph read is ever written around it.
4. Where the money is — the chat model on Ollama Cloud and the Jev calls a run makes — no test shows that a stop
   cancels the call in flight rather than waiting for it, and `chat-agent` only asks that such calls be "abandoned".

## What Changes

- **Mutations are not aborted; what they start is stopped by its id.** The `web-ui` requirement says so: a query's
  request carries the signal react-query gives it; a mutation runs to its answer, and the work it started is stopped
  through that work's own cancel route. The control that started it cannot start it twice meanwhile.
- **Esc stops a loading read on the telemetry, Jev statistics, evals and A2A admin screens**, with the "Esc to stop"
  hint while it loads, the same way the topology and compliance screens already do.
- **An architecture test** fails the build when a Neo4j driver call in the graph classes is not inside a query handed
  to `GraphStop` (its own terminate statement aside).
- **A stopped chat run cancels its paid calls:** `chat-agent` changes "abandoned" to "cancelled", and tests show that
  a run stopped while the model is answering, and one stopped while a Jev call is out, cancel that call's request, and
  that no model output is read after the stop.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `web-ui`: "Every request the web makes can be aborted" — queries carry the signal; mutations are not aborted and the
  work they start is stopped by its id; Esc stops the loading reads of the four screens.
- `chat-agent`: "A stopped run stops its work" — model and Jev calls in flight are cancelled, not abandoned.
- `graph-store`: a requirement that every query is run so that a stop ends it in Neo4j, enforced by a test.

## Impact

- Web: `telemetry/TelemetryPage.tsx`, `jev/JevPage.tsx`, `evals/EvalsPage.tsx`, `admin/A2AAdminPage.tsx` (Esc and the
  hint), with Vitest tests.
- Tests: `tests/Maf.Lab.Tests/GraphStoreTests.cs` (the architecture test); `tests/Shared/ScriptedChatClient.cs` and
  `tests/Maf.Lab.Tests/FakeJev.cs` (a way to hold a call until it is cancelled); a chat stop test in
  `RunProtocolTests.cs`.
- No product code changes are expected for 3 and 4: they prove what is there. If a test shows a call is not cancelled,
  the fix is part of this change.

## Progress

None — this change adds no CLI tool, `make` target or UI action; the reads whose Esc it adds already show progress.

## Stopping

- Key: Esc on the telemetry, Jev statistics, evals and A2A admin screens while a read is loading
- Stop: the query's abort signal (react-query's `cancelQueries`), which ends the request
- Recorded in: the request itself — these reads hold no state of their own
- Shown: "Esc to stop" while loading; once stopped, the screen keeps what it last showed

## Documentation impact

None. No document describes these screens' keys, the mutation rule, or the test setup; no route, target, project,
model or lb location changes.

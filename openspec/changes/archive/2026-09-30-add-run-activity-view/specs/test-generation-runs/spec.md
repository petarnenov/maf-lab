## MODIFIED Requirements

### Requirement: Progress to the browser over SSE
The api SHALL offer, to any signed-in user, one event stream per run in the AG-UI protocol. Nothing about a run SHALL
reach the browser in any other form. Every event SHALL name the run (`runId` is the run's id and `threadId` is
`testgen:<run id>`). The stream SHALL begin with `RUN_STARTED`, followed by a `STATE_SNAPSHOT` holding the run's
summary: state, current phase, attempt n of N, latest coverage, target, model, tokens and cost. It SHALL then carry
every activity entry recorded so far, in order, as the protocol's own events:
- a phase: `STEP_FINISHED` for the previous step and `STEP_STARTED` named for the attempt and phase;
- a tool call: `TOOL_CALL_START`, `TOOL_CALL_ARGS` (the path it concerned), `TOOL_CALL_END` and `TOOL_CALL_RESULT`
  (outcome and summary);
- model text: `TEXT_MESSAGE_START`, a `TEXT_MESSAGE_CONTENT` per chunk, and `TEXT_MESSAGE_END` once the message
  is done;
- reasoning: the protocol's `REASONING_*` events, in the same shape;
- an attempt's result: a `CUSTOM` event named `maf-lab/testgen-attempt`.

After the backlog, the stream SHALL carry live events as they are recorded, and a new `STATE_SNAPSHOT` on every
change of the summary. It SHALL end with exactly one terminal event when the agent's work on the run is over:
`RUN_FINISHED` with the summary as its result when the run is a candidate or ended any other way than failed or
canceled, and `RUN_ERROR` with the reason as its code when it failed or was canceled. Nothing SHALL follow the
terminal event. For a run that has already ended, the stream SHALL replay the run and end at once. The browser SHALL
never talk to the agent.

#### Scenario: Late subscriber
- **WHEN** the browser subscribes during attempt 3
- **THEN** it receives `RUN_STARTED`, a `STATE_SNAPSHOT` with attempt 3, the events of every recorded entry of attempts 1 to 3 in order, then live events

#### Scenario: Phase in the state
- **WHEN** the agent reports that attempt 2 is building
- **THEN** the stream carries `STEP_STARTED` for attempt 2 building and a `STATE_SNAPSHOT` with phase `building` and attempt 2

#### Scenario: Tool call as protocol events
- **WHEN** the agent reports a run-tests call with build ok, 12 passed, 1 failed at 72.1%
- **THEN** the stream carries `TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_END` and `TOOL_CALL_RESULT` for one tool call id, the result holding that outcome

#### Scenario: A failed run ends in error
- **WHEN** a run ends `failed` with reason `runner_unavailable`
- **THEN** the stream's last event is `RUN_ERROR` with code `runner_unavailable`, and nothing follows it

#### Scenario: Replaying a finished run
- **WHEN** a user opens the stream of a run that ended `candidate` an hour ago
- **THEN** the stream replays the whole run in order, ends with `RUN_FINISHED`, and closes

## ADDED Requirements

### Requirement: Activity record
The api SHALL persist every activity entry the agent reports for a run, ordered by the entry's sequence number, and
SHALL store an entry at most once, even when a resubscription replays it or two replicas see it. A continuation chunk
SHALL be appended to its entry. The record SHALL be kept with the run after the run ends, capped at 2 000 entries and
1 MB per run. Beyond the cap, the api SHALL keep the newest entries and record that older ones were dropped. The
stream SHALL say so with a `CUSTOM` event named `maf-lab/testgen-activity-dropped`. The record SHALL never be written
to logs, spans or metrics.

#### Scenario: Replayed updates
- **WHEN** the follower resubscribes and the agent replays entries 40 to 45 already stored
- **THEN** the record still holds each of them once

#### Scenario: Another replica serves the stream
- **WHEN** replica A follows the task and the browser's stream is served by replica B
- **THEN** replica B relays the entries A persisted, in order, within a few seconds

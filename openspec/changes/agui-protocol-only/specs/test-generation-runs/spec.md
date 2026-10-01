## MODIFIED Requirements

### Requirement: Progress to the browser over SSE
The api SHALL offer, to any signed-in user, each run as an AG-UI agent that the browser follows with the protocol's own
client. Nothing about a run SHALL reach the browser in any other form. A run SHALL be followed on the thread
`testgen:<run id>`, which a page MAY suffix with `:<viewer>` so that each page following the run has a thread of its
own; every event SHALL name that thread. The stream SHALL begin with `RUN_STARTED`, followed by a
`STATE_SNAPSHOT` holding the run's state. That state SHALL contain:
- the summary: state, reason, current phase, attempt n of N, latest coverage, target, model, budget, tokens and cost;
- `attempts`: the result of every attempt finished so far, in order. When the entry records them, an attempt SHALL
  carry what it ran (scope, related test files, tests run, fallback reason, reused) and its whole-suite confirmation
  (tests, coverage, reused). It SHALL omit them for an entry recorded without them;
- `stop`: once the agent has stopped, the stop reason, the last attempt, the best coverage and, for `budget`, the
  attempt not started; absent before;
- `resumes`: the attempts at which the agent took the run over again after a restart, in order;
- `dropped`: true when the oldest activity of the run was dropped to keep it within its cap.

It SHALL then carry every activity entry recorded so far, in order, as the protocol's own events:
- a phase: `STEP_FINISHED` for the previous step and `STEP_STARTED` named for the attempt and phase;
- a tool call: `TOOL_CALL_START`, `TOOL_CALL_ARGS` (the path it concerned), `TOOL_CALL_END` and `TOOL_CALL_RESULT`
  (outcome and summary);
- model text: `TEXT_MESSAGE_START`, a `TEXT_MESSAGE_CONTENT` per chunk, and `TEXT_MESSAGE_END` once the message
  is done;
- reasoning: the protocol's `REASONING_*` events, in the same shape;
- an attempt's result, the agent's stop, its takeover after a restart and a dropped record: a change of the state
  (`STATE_DELTA` or `STATE_SNAPSHOT`) at the point it happened. A stop and a takeover SHALL each be preceded by
  `STEP_FINISHED` for the open step.

The browser SHALL show a takeover in the run's timeline as a notice that the agent restarted and resumed at that
attempt, and the stop as the timeline's closing entry.

After the backlog, the stream SHALL carry live events as they are recorded, and a state change on every change of the
summary. It SHALL end with exactly one terminal event when the agent's work on the run is over, after a state that says
how it ended: `RUN_FINISHED` when the run is a candidate or ended any other way than failed or canceled, and `RUN_ERROR`
when it failed or was canceled. The reason SHALL be read from the state, whose summary carries it. Nothing SHALL follow the terminal event. For a run that
has already ended, the stream SHALL replay the run and end at once. No event SHALL be `CUSTOM`. The browser SHALL
never talk to the agent.

#### Scenario: Late subscriber
- **WHEN** the browser subscribes during attempt 3
- **THEN** it receives `RUN_STARTED`, a `STATE_SNAPSHOT` with attempt 3 and the results of attempts 1 and 2, the events
  of every recorded entry of attempts 1 to 3 in order, then live events

#### Scenario: Phase in the state
- **WHEN** the agent reports that attempt 2 is building
- **THEN** the stream carries `STEP_STARTED` for attempt 2 building and a state change with phase `building` and
  attempt 2

#### Scenario: Tool call as protocol events
- **WHEN** the agent reports a run-tests call with build ok, 12 passed, 1 failed at 72.1%
- **THEN** the stream carries `TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_END` and `TOOL_CALL_RESULT` for one tool
  call id, the result holding that outcome

#### Scenario: An attempt's scope in the event
- **WHEN** the agent reports attempt 2 that ran 58 tests from 3 related files and was confirmed on the whole suite with
  1219 tests
- **THEN** the state's attempt 2 carries scope `related`, 3 files, 58 tests, and a confirmation with 1219 tests

#### Scenario: An attempt recorded before scopes
- **WHEN** a run recorded before attempts carried their scope is replayed
- **THEN** its attempts in the state carry no scope and no confirmation, and nothing else about them changes

#### Scenario: The stop closes the timeline
- **WHEN** the agent stops for `budget` before attempt 3 and the run ends `completed_no_change`
- **THEN** the stream carries `STEP_FINISHED` for attempt 2 measuring, a state change whose `stop` has reason `budget`
  and state `completed_no_change`, then `RUN_FINISHED`

#### Scenario: The agent restarted
- **WHEN** the agent restarts during attempt 2 and resumes the task
- **THEN** the stream carries `STEP_FINISHED` for attempt 2 generating, a state change adding attempt 2 to `resumes`,
  then `STEP_STARTED` for attempt 2 generating, and the timeline shows the restart notice

#### Scenario: A failed run ends in error
- **WHEN** a run ends `failed` with reason `runner_unavailable`
- **THEN** the stream's last event is `RUN_ERROR`, the state before it carries state `failed` and reason
  `runner_unavailable`, and nothing follows it

#### Scenario: Replaying a finished run
- **WHEN** a user opens a run that ended `candidate` an hour ago
- **THEN** the stream replays the whole run in order, ends with `RUN_FINISHED`, and closes

#### Scenario: No custom events
- **WHEN** any run is replayed
- **THEN** no event on its stream is `CUSTOM`

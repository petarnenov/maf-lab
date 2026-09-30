## MODIFIED Requirements

### Requirement: The api is the only A2A client
Runs SHALL be started only through the api. The api SHALL create one long-running A2A task on the test-generation
agent per run and SHALL persist the run with its task id and its budget. It SHALL follow the general rules of outbound
A2A consultation: discovery by card, the assistant's own service credentials, a deadline, and an audit record of each
operation without content. A run's deadline SHALL be long enough for 5 attempts and SHALL be configurable. The run's
budget is the one the administrator chose at start: a token cap, a cost cap, both, or neither (unlimited). The api
SHALL pass exactly that budget to the task and SHALL NOT add a default cap of its own.

#### Scenario: Start
- **WHEN** an administrator starts a run with a valid model
- **THEN** the api creates the A2A task, persists the run with the task id, and returns the run as `submitted`

#### Scenario: Start with a budget
- **WHEN** an administrator starts a run with a cost cap of $0.50 and no token cap
- **THEN** the task carries a cost cap of $0.50 and no token cap, and the run's summary shows that budget

#### Scenario: Start without a budget
- **WHEN** an administrator starts a run without a budget
- **THEN** the task carries no caps and the run's summary shows the budget as unlimited

#### Scenario: Invalid budget
- **WHEN** a start request carries a token cap of 0 or a negative cost cap
- **THEN** it is rejected as invalid, no run is created and the threshold is unchanged

#### Scenario: Agent unreachable
- **WHEN** the agent's card or endpoint cannot be reached at start
- **THEN** the start fails with "agent unavailable", no active run remains, and an audit record says unreachable

### Requirement: Run states
A run SHALL be in one of these states:

- `submitted` or `working`, mirrored from the A2A task;
- `verifying`, after the task completes, while the api checks the result;
- `candidate`, verified and on its branch, awaiting a decision;
- `accepted`, merged into `main`;
- `discarded`;
- `completed_no_change`, when the task completed with an empty diff;
- `failed` (with a reason), `canceled` or `verification_failed` (with a reason).

`accepted`, `discarded`, `completed_no_change`, `failed`, `canceled` and `verification_failed` SHALL be final.
Every run SHALL carry the attempt count, the latest coverage, the model, the budget, the tokens used and the cost.
When a task completes, the run SHALL keep the task's stop reason (`target`, `attempts` or `budget`) as its reason,
both for `completed_no_change` and for a candidate. A reason that verification later sets SHALL replace it.

#### Scenario: States in order
- **WHEN** a run reaches its target and passes verification
- **THEN** it has passed through `submitted`, `working`, `verifying` and is now `candidate` with reason `target`

#### Scenario: No change keeps why
- **WHEN** a task completes with an empty diff and stop reason `budget`
- **THEN** the run ends `completed_no_change` with reason `budget`

### Requirement: Progress to the browser over SSE
The api SHALL offer, to any signed-in user, one event stream per run in the AG-UI protocol. Nothing about a run SHALL
reach the browser in any other form. Every event SHALL name the run (`runId` is the run's id and `threadId` is
`testgen:<run id>`). The stream SHALL begin with `RUN_STARTED`, followed by a `STATE_SNAPSHOT` holding the run's
summary: state, reason, current phase, attempt n of N, latest coverage, target, model, budget, tokens and cost. It
SHALL then carry every activity entry recorded so far, in order, as the protocol's own events:
- a phase: `STEP_FINISHED` for the previous step and `STEP_STARTED` named for the attempt and phase;
- a tool call: `TOOL_CALL_START`, `TOOL_CALL_ARGS` (the path it concerned), `TOOL_CALL_END` and `TOOL_CALL_RESULT`
  (outcome and summary);
- model text: `TEXT_MESSAGE_START`, a `TEXT_MESSAGE_CONTENT` per chunk, and `TEXT_MESSAGE_END` once the message
  is done;
- reasoning: the protocol's `REASONING_*` events, in the same shape;
- an attempt's result: a `CUSTOM` event named `maf-lab/testgen-attempt`;
- the agent's stop: `STEP_FINISHED` for the open step, then a `CUSTOM` event named `maf-lab/testgen-stopped` with the
  stop reason, the last attempt, the best coverage and, for `budget`, the attempt not started.

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

#### Scenario: The stop closes the timeline
- **WHEN** the agent stops for `budget` before attempt 3 and the run ends `completed_no_change`
- **THEN** the stream carries `STEP_FINISHED` for attempt 2 measuring, `CUSTOM maf-lab/testgen-stopped` with reason `budget`, a `STATE_SNAPSHOT` with state `completed_no_change` and reason `budget`, then `RUN_FINISHED`

#### Scenario: A failed run ends in error
- **WHEN** a run ends `failed` with reason `runner_unavailable`
- **THEN** the stream's last event is `RUN_ERROR` with code `runner_unavailable`, and nothing follows it

#### Scenario: Replaying a finished run
- **WHEN** a user opens the stream of a run that ended `candidate` an hour ago
- **THEN** the stream replays the whole run in order, ends with `RUN_FINISHED`, and closes

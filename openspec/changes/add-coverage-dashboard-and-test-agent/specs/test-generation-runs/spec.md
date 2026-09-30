# Spec Delta

## Purpose

The api's side of a test-generation run. It starts and follows the agent's A2A task, relays progress to the browser,
checks the result independently, and puts accepted tests into `main`.

## ADDED Requirements

### Requirement: The api is the only A2A client
Runs SHALL be started only through the api. The api SHALL create one long-running A2A task on the test-generation
agent per run and SHALL persist the run with its task id. It SHALL follow the general rules of outbound A2A
consultation: discovery by card, the assistant's own service credentials, a deadline, and an audit record of each
operation without content. A run's deadline SHALL be long enough for 5 attempts and SHALL be configurable.

#### Scenario: Start
- **WHEN** an administrator starts a run with a valid model
- **THEN** the api creates the A2A task, persists the run with the task id, and returns the run as `submitted`

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
Every run SHALL carry the attempt count, the latest coverage, the model, the tokens used and the cost.

#### Scenario: States in order
- **WHEN** a run reaches its target and passes verification
- **THEN** it has passed through `submitted`, `working`, `verifying` and is now `candidate`

### Requirement: Following the task
The api SHALL follow the task by subscribing to its updates. When the stream drops, it SHALL resubscribe. When
resubscribing fails, it SHALL fall back to polling the task's state until the stream can be restored or the task ends.
Every update SHALL be persisted before it is relayed. A run SHALL survive restart of the api replica that started it:
another replica SHALL pick it up from the persisted task id.

#### Scenario: Stream drops
- **WHEN** the update stream closes during attempt 2
- **THEN** the api resubscribes or polls, and no attempt update is lost from the run's record

#### Scenario: Replica restarts
- **WHEN** the replica following a run is restarted
- **THEN** the run continues to be followed and ends in the right final state

#### Scenario: Deadline passes
- **WHEN** a run's deadline passes while the task is still working
- **THEN** the api cancels the task and the run ends `failed` with reason `deadline`

### Requirement: Progress to the browser over SSE
The api SHALL offer, to any signed-in user, a server-sent event stream of a run's updates: state, attempt n of N,
latest coverage, tokens and cost. On connect it SHALL first send the run's full current state. The stream SHALL end
when the run reaches a final state. The browser SHALL never talk to the agent.

#### Scenario: Late subscriber
- **WHEN** the browser subscribes during attempt 3
- **THEN** its first event is the run's current state with attempt 3, followed by live updates

### Requirement: Cancel
An administrator SHALL be able to cancel an active run. The api SHALL send an A2A cancel for the task. The run SHALL end
`canceled`, the threshold stays as saved, and no branch is created.

#### Scenario: Cancel mid-run
- **WHEN** an administrator cancels during attempt 3
- **THEN** the task is canceled, the run ends `canceled`, and no diff is applied

### Requirement: Independent verification
When the task completes with a non-empty diff, the api SHALL NOT trust the coverage numbers the agent reported. It
SHALL:

1. check that the diff touches only allowlisted test paths;
2. re-check the test guardrails on the diff;
3. have the coverage runner apply the diff at the task's commit and run the toolchain's tests with coverage in a fresh
   workspace;
4. require the build and every test to pass.

If any step fails, the run SHALL end `verification_failed` with the reason, and coverage SHALL NOT change. If all
steps pass, the api SHALL ingest the runner's report as a candidate snapshot linked to the run.

#### Scenario: Verification failure
- **WHEN** the agent reports 88% but the runner's run has a failing test
- **THEN** the run ends `verification_failed` with that reason and the file's coverage is unchanged

#### Scenario: Diff outside the allowlist
- **WHEN** the returned diff modifies a production file
- **THEN** the run ends `verification_failed` without running anything

#### Scenario: Measured, not reported
- **WHEN** the agent reports 88% and the runner measures 84%
- **THEN** the candidate shows 84%

### Requirement: Candidate branch
A verified run SHALL have its diff committed onto a branch `test-agent/<file-slug>-<runId>`, based on the task's
commit, with a message naming the run, the file and the coverage change. The candidate SHALL be shown as candidate
until the run is accepted or discarded.

#### Scenario: Branch created
- **WHEN** verification passes
- **THEN** the branch exists with one commit on top of the task's commit containing exactly the diff

### Requirement: Accept merges into main, safely
An administrator SHALL be able to accept a candidate, which merges its branch into `main`. The merge SHALL succeed
only if it has no conflicts. The update of `main` SHALL be compare-and-swap: if `main` moved between reading and
writing, the merge SHALL be retried once against the new `main`. The merge SHALL be refused, leaving `main` and every
working tree untouched, if `main` is checked out in a working tree that has uncommitted changes. On success, the run
SHALL become `accepted` and its candidate snapshot SHALL become official at the merge commit. An administrator SHALL
be able to discard a candidate instead, which deletes its branch and ends the run `discarded`.

#### Scenario: Accept
- **WHEN** an administrator accepts a candidate and `main` merges cleanly
- **THEN** `main` contains the tests, the run is `accepted` and the file's coverage is the candidate's

#### Scenario: Conflict
- **WHEN** the branch conflicts with `main`
- **THEN** the accept is refused with "merge conflict", `main` is unchanged, and the run stays `candidate`

#### Scenario: Dirty checkout
- **WHEN** `main` is checked out in a working tree with uncommitted changes
- **THEN** the accept is refused with that reason and nothing is written

#### Scenario: Discard
- **WHEN** an administrator discards a candidate
- **THEN** its branch is deleted, the run ends `discarded` and coverage is unchanged

### Requirement: One trace per run
A run SHALL be traceable end to end: the start request, the A2A task, the agent's attempts, runner requests and model
calls SHALL share one trace through propagated context. Telemetry SHALL carry structure (states, attempts, counts,
durations, tokens, cost), never prompts, source text or diffs.

#### Scenario: Trace spans services
- **WHEN** a run completes
- **THEN** the trace store holds one trace containing spans from the api, the agent, the runner and the model calls

#### Scenario: No content in telemetry
- **WHEN** the run's spans and logs are inspected
- **THEN** none contains a prompt, a line of source, or the diff

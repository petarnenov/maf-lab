# test-generation-runs Specification

## Purpose
The api's side of a test-generation run. It starts and follows the agent's A2A task, relays progress to the browser,
checks the result independently, and puts accepted tests into `main`.

## Requirements

### Requirement: The api is the only A2A client
Runs SHALL be started only through the api. The api SHALL create one long-running A2A task on the test-generation
agent per run and SHALL persist the run with its task id and its budget. It SHALL follow the general rules of outbound
A2A consultation: discovery by card, the assistant's own service credentials, a deadline, and an audit record of each
operation without content. A run's deadline SHALL be long enough for the attempt cap (10 attempts) and SHALL be configurable. The run's
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

#### Scenario: A long run is not cut short
- **WHEN** a run is still working in attempt 9, an hour after it started
- **THEN** it is not canceled for its deadline

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
The attempt count SHALL be the last attempt that started: an attempt the budget stops before it finishes still
counts, and the task's final report SHALL NOT lower the count.
When a task completes, the run SHALL keep the task's stop reason (`target`, `attempts` or `budget`) as its reason,
both for `completed_no_change` and for a candidate. A reason that verification later sets SHALL replace it.

#### Scenario: States in order
- **WHEN** a run reaches its target and passes verification
- **THEN** it has passed through `submitted`, `working`, `verifying` and is now `candidate` with reason `target`

#### Scenario: No change keeps why
- **WHEN** a task completes with an empty diff and stop reason `budget`
- **THEN** the run ends `completed_no_change` with reason `budget`

#### Scenario: Budget spent during the first attempt
- **WHEN** a task's budget is spent during attempt 1 and the task completes with no finished attempt
- **THEN** the run ends with attempt 1 and reason `budget`, not attempt 0

#### Scenario: Stopped before an attempt starts
- **WHEN** a task stops for `budget` before attempt 3, after attempt 2 finished
- **THEN** the run ends with attempt 2

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
  stop reason, the last attempt, the best coverage and, for `budget`, the attempt not started;
- the agent's takeover after a restart: `STEP_FINISHED` for the open step, then a `CUSTOM` event named
  `maf-lab/testgen-resumed` with the attempt it resumes at. The browser SHALL show it in the run's timeline as a notice
  that the agent restarted and resumed at that attempt.

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

#### Scenario: The agent restarted
- **WHEN** the agent restarts during attempt 2 and resumes the task
- **THEN** the stream carries `STEP_FINISHED` for attempt 2 generating, `CUSTOM maf-lab/testgen-resumed` with attempt
  2, then `STEP_STARTED` for attempt 2 generating, and the timeline shows the restart notice

#### Scenario: A failed run ends in error
- **WHEN** a run ends `failed` with reason `runner_unavailable`
- **THEN** the stream's last event is `RUN_ERROR` with code `runner_unavailable`, and nothing follows it

#### Scenario: Replaying a finished run
- **WHEN** a user opens the stream of a run that ended `candidate` an hour ago
- **THEN** the stream replays the whole run in order, ends with `RUN_FINISHED`, and closes

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
4. require the build, including the lint bar the runner holds the diff's files to, and every test to pass.

If any step fails, the run SHALL end `verification_failed` with the reason; a build that failed only on lint
diagnostics SHALL be named as not passing lint. Coverage SHALL NOT change. If all steps pass, the api SHALL ingest the
runner's report as a candidate snapshot linked to the run. The run that proves a suspected bug, whose diff is the
candidate's with that one test un-skipped and is never merged, SHALL be judged by its tests alone: lint diagnostics on
it SHALL NOT fail the check.

#### Scenario: Verification failure
- **WHEN** the agent reports 88% but the runner's run has a failing test
- **THEN** the run ends `verification_failed` with that reason and the file's coverage is unchanged

#### Scenario: A candidate that would fail lint
- **WHEN** every test passes but the runner reports the build failed with only a `warning CA2022` diagnostic in the
  diff's test file
- **THEN** the run ends `verification_failed` with the reason that the tests do not pass lint, and no candidate is
  recorded

#### Scenario: A suspected bug's proof with a lint finding
- **WHEN** the run that un-skips a suspected bug's test fails that test and reports only lint diagnostics
- **THEN** the bug counts as reproduced and verification goes on to measure the candidate

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

### Requirement: Suspected bugs become GitHub issues after verification
For each suspected bug in a completed task's report, the api SHALL check the claim independently. A skip is accepted
only if the test's skip marker is on the list of suspected bugs. With the skip removed, the runner SHALL run the test
against the task's commit, and the test SHALL fail.

- A suspected bug whose test passes once un-skipped is not a bug. The run SHALL end `verification_failed` with that
  reason, and no issue is created.
- For each confirmed suspected bug, the api SHALL create one issue in the repository's GitHub project. The issue SHALL
  carry:
  - the title and description;
  - the expected and actual behaviour and the failure message;
  - the test and its file;
  - the target file, the run, the commit and the model;
  - a label saying it was reported by the test agent.
- The api SHALL write the issue's link into that test's skip marker before the candidate branch is committed, so the
  skipped test in the repository points at its issue.
- Creating the issue SHALL happen at most once per run and test, even if verification is repeated after a restart.
- Issues SHALL be created with a credential that the api alone holds, limited to the repository's issues. It is never
  given to the agent or the runner, and never logged.
- Without that credential, the skip marker SHALL say that no issue was created, and the run SHALL proceed.

The Coverage screen SHALL show a run's suspected bugs with links to their issues.

When a candidate is discarded, each issue it created SHALL be closed with a comment saying the run was discarded.
When a candidate is accepted, each issue SHALL get a comment linking the merge commit, and SHALL stay open for a person
to resolve.

#### Scenario: Confirmed bug
- **WHEN** the report lists one suspected bug and its test fails once un-skipped
- **THEN** one issue is created with the description, expected and actual behaviour, the test, the run and the commit,
  and the candidate branch's skip reason carries the issue link

#### Scenario: Bug not reproduced
- **WHEN** a suspected bug's test passes once un-skipped
- **THEN** the run ends `verification_failed` with reason "suspected bug not reproduced", and no issue is created

#### Scenario: Skip that is not a suspected bug
- **WHEN** the diff skips a test that the report does not list as a suspected bug
- **THEN** the run ends `verification_failed` without running anything

#### Scenario: Verification repeated
- **WHEN** the api restarts during verification and verifies the same run again
- **THEN** no second issue is created for the same test

#### Scenario: No GitHub credential
- **WHEN** no issue credential is configured and a bug is confirmed
- **THEN** the test is skipped with a marker saying no issue was created, and the run still becomes a candidate

#### Scenario: Discarded run
- **WHEN** a candidate with one created issue is discarded
- **THEN** the issue is closed with a comment saying the run that found it was discarded

#### Scenario: Screen shows the bugs
- **WHEN** a run with a created issue is a candidate
- **THEN** the file view lists the suspected bug with a link to its issue

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

### Requirement: Per-run limits
A start request MAY carry limits: max attempts, tool rounds per attempt, test runs per attempt, the run deadline in
minutes and the suspected bugs the run may report. A limit that is absent SHALL take its default (10, 40, 2, the
configured deadline and 3). The api SHALL reject a limit outside its bounds (attempts 1–10, tool rounds 1–40, test runs
0–2, deadline from 10 minutes to the configured deadline, suspected bugs 0–3) as invalid, before any run is created and
without changing the threshold. The api SHALL store the limits on the run, SHALL pass the attempt, round, test-run and
suspected-bug limits to the task, and SHALL return all of them in the run's summary (a run without its own deadline shows none, meaning the configured one). The api SHALL cancel a run when
its own deadline passes, and SHALL verify a candidate against the run's own suspected-bug limit. A run stored before
limits existed SHALL read as having the defaults.

#### Scenario: Start with limits
- **WHEN** an administrator starts a run with max attempts 4 and 20 tool rounds per attempt
- **THEN** the task carries max attempts 4, 20 tool rounds and 2 test runs per attempt, and the run's summary shows them

#### Scenario: Start without limits
- **WHEN** a start request carries no limits
- **THEN** the task carries max attempts 10, 40 tool rounds, 2 test runs per attempt and 3 suspected bugs, and the run's deadline is the configured one

#### Scenario: Limit out of bounds
- **WHEN** a start request asks for 11 attempts, 0 tool rounds, a 5-minute deadline or 4 suspected bugs
- **THEN** it is rejected as invalid, no run is created and the threshold is unchanged

#### Scenario: The run's own deadline
- **WHEN** a run started with a 30-minute deadline is still working 31 minutes after it started
- **THEN** it is canceled and fails with reason `deadline`

#### Scenario: Verified with the run's bug limit
- **WHEN** a run started with a suspected-bug limit of 0 returns a candidate that reports a suspected bug
- **THEN** its verification fails on a guardrail, no proof run starts, and no issue is opened

### Requirement: Which tests verification runs
The api SHALL prove each suspected bug with a runner job for the related tests only: the diff with that test
un-skipped, the run's file as the target. The test is in a file the diff adds or changes, so it is always among them.

The verification run SHALL run the whole suite. A diff whose tests break, or are broken by, a test it did not touch
then still ends `verification_failed`. The candidate's coverage SHALL be that run's measured coverage. Coverage
refresh SHALL also run the whole suite.

#### Scenario: Proof runs the related tests
- **WHEN** a completed run reports one suspected bug
- **THEN** the api asks the runner for the related tests with that test un-skipped, and the test is among them

#### Scenario: Verification runs everything
- **WHEN** the api verifies a completed run
- **THEN** the measured run's scope is the whole suite, and a failing test anywhere in it ends the run
  `verification_failed`

### Requirement: When a run's work ended
A run SHALL record when its work ended: the first time it leaves the running states (`submitted`, `working`,
`verifying`), whether into `candidate` or into a final state. A later move of the run — accepting or discarding a
candidate — SHALL NOT change it, and a run that is still running SHALL have none.

A run stored before this was recorded SHALL get it once, at startup, from the time of its first recorded update in a
state other than a running one, or, when it has no such update, from its last change. A running run SHALL be left
without one.

#### Scenario: A candidate accepted later
- **WHEN** a run reaches `candidate` at 10:07 and is accepted at 11:30
- **THEN** its work ended at 10:07

#### Scenario: A run that fails
- **WHEN** a working run fails at the deadline
- **THEN** its work ended when it failed

#### Scenario: Still running
- **WHEN** a run is `working`
- **THEN** it has no end

#### Scenario: A run stored before ends were recorded
- **WHEN** the api starts with an accepted run that has no end and whose updates show it became `candidate` at 10:07
- **THEN** its work ended at 10:07, and a working run stored alongside it still has no end

### Requirement: Repository writes keep the developer's ownership
Committing a run's candidate branch, accepting it (whether `main` is merged in the checkout that has it or in a
temporary working tree) and discarding it SHALL leave every path they create or change in the repository — objects,
refs, reflogs, the index and working-tree files — owned by the user who runs the stack, never by root or by a service
account. After any of them, that user SHALL be able to commit, branch and merge in the repository as before.

#### Scenario: Accept on a Linux host
- **WHEN** an administrator accepts a candidate and `main` is checked out in the developer's clean checkout on Linux
- **THEN** the merged test files, the new objects, `main`'s ref and its reflog are owned by the developer, and the developer's next `git commit` succeeds

#### Scenario: A run's candidate branch
- **WHEN** a verified run is committed onto `test-agent/<file-slug>-<runId>`
- **THEN** the branch's ref, its reflog and the objects written for it are owned by the developer

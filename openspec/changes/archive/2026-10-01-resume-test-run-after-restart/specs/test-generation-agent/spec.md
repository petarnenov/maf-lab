# Spec Delta

## ADDED Requirements

### Requirement: A task survives a restart of the agent
A task SHALL belong to the shared store, not to the agent process running it. While a replica runs a task it SHALL
hold a lease on the task in the shared store and renew it; a lease that is not renewed SHALL lapse within one minute.
The agent SHALL keep a checkpoint of the task in the shared store: the request as soon as the task is accepted, and,
after the baseline and after every finished attempt, what the next attempt needs — the baseline coverage, the attempt
log so far, the best result with its diff and suspected bugs, the tests written so far, the feedback for the next
attempt, the tokens and cost used, and the last activity sequence number. The checkpoint SHALL NOT carry the API key.

On start, and then periodically, the agent SHALL look for tasks that are `submitted` or `working`, whose lease has
lapsed. It SHALL take each over (at most one replica at a time) and resume it from its checkpoint:
- the work of the last finished attempt SHALL be kept and the baseline SHALL NOT be measured again once recorded;
- an attempt that was interrupted SHALL run again from the end of the last finished attempt, as the same attempt
  number;
- the attempt cap, the budget (counting the tokens and cost already recorded) and the deadline SHALL apply to the task
  as a whole, not restart;
- the task SHALL end in the same final states as a task that ran without a restart.

A task that cannot be resumed — it has no checkpoint, or its workspace cannot be rebuilt at its commit — SHALL end
`failed` with reason `interrupted`. A task SHALL never stay `working` without a replica running it for longer than the
lease plus one takeover period. A canceled task SHALL NOT be resumed. The checkpoint and the lease SHALL be removed
when the task ends.

#### Scenario: Restart during an attempt
- **WHEN** the agent stops during attempt 2 of 10, after attempt 1 finished at 62%, and starts again
- **THEN** it takes the task over, records a `resumed` entry for attempt 2, runs attempt 2 from the tests attempt 1
  left, and the task goes on to a final state with a report whose attempt log holds attempt 1 once

#### Scenario: Restart during the baseline
- **WHEN** the agent stops while the baseline is being measured, and starts again
- **THEN** it takes the task over and measures the baseline, then runs attempt 1

#### Scenario: The budget carries over
- **WHEN** a task with a 100 000-token cap used 80 000 tokens before a restart, and the next attempt is estimated at
  30 000
- **THEN** after the takeover that attempt is not started and the task completes with stop reason `budget`

#### Scenario: Nothing to resume from
- **WHEN** a `working` task has no checkpoint and its lease has lapsed
- **THEN** the agent ends it `failed` with reason `interrupted`, and the api's run ends `failed` with reason
  `interrupted`

#### Scenario: A live task is not taken
- **WHEN** one replica is running a task and renewing its lease, and another replica looks for tasks to take over
- **THEN** the other replica leaves the task alone

#### Scenario: A canceled task stays canceled
- **WHEN** a task was canceled before the agent restarted
- **THEN** the agent does not resume it

## MODIFIED Requirements

### Requirement: Activity reporting
While it works on a task, the agent SHALL report its activity to the api over A2A, as artifacts of the task, so that
the task as it stands holds every entry so far and a caller that reconnects or polls misses none. Each entry SHALL
carry a sequence number that increases within the task, the time, the attempt, and one kind:
- `phase`: the attempt entered `generating`, `building`, `testing` or `measuring`;
- `tool`: a tool call, with the tool name, the repository path it concerned (when any), and a structured outcome
  summary (for example build result, test counts and coverage for running tests, or refused with the reason for a
  write outside the allowlist). It SHALL never carry file contents or the diff;
- `attempt`: an attempt's result, with coverage before and after, the build outcome, test counts, and at most ten
  error lines;
- `text` and `reasoning`: the model's reply and, when the provider returns it, its reasoning. They SHALL be sent in
  chunks as they stream, no more than about every two seconds, with each chunk appended to the entry it continues;
- `stopped`: the task's work is over, with the stop reason, the last attempt that ran, the best coverage, and, for
  `budget`, the attempt not started. It SHALL be the last entry of a completed task;
- `resumed`: the agent took the task over after a restart, with the attempt it resumes at. Entries after it SHALL carry
  higher sequence numbers than every entry recorded before the restart.

A text or reasoning entry SHALL be capped at 4 KB and marked truncated beyond it. Reporting SHALL NOT slow down or
fail the task: if an update cannot be sent, the agent SHALL go on and send later entries. None of this content SHALL
appear in the agent's logs, spans or metrics.

#### Scenario: A tool call is reported
- **WHEN** the model calls the run-tests tool in attempt 2 and the build succeeds with 12 passed, 1 failed at 72.1%
- **THEN** a `tool` entry for attempt 2 names the tool and carries build ok, 12 passed, 1 failed and 72.1%, and no source text

#### Scenario: A refused write is reported
- **WHEN** the model asks to write `src/Maf.Lab.Api/Program.cs`
- **THEN** a `tool` entry records the write as refused with its path and the reason, and the run continues

#### Scenario: Model text streams
- **WHEN** the model streams a reply for eight seconds
- **THEN** the api receives the reply as several chunks of one `text` entry, not one message at the end

#### Scenario: The stop is reported
- **WHEN** the task stops for `budget` before attempt 3
- **THEN** the last entry is `stopped` with reason `budget`, last attempt 2 and attempt not started 3

#### Scenario: The resume is reported
- **WHEN** the agent restarts during attempt 3 and takes the task over
- **THEN** the next entry is `resumed` with attempt 3, numbered after the last entry recorded before the restart

#### Scenario: No content in telemetry
- **WHEN** the agent's logs and spans for a run are inspected
- **THEN** none contains the model's text, its reasoning or a tool's path summary

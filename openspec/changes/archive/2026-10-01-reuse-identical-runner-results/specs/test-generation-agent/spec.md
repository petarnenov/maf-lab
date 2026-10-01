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
  error lines. It SHALL also say what the attempt's measured run ran: the scope it used (`related` or `all`), the
  number of related test files, the number of tests that ran, the reason when the whole suite ran instead of the
  related tests, and whether the runner reused an earlier result. When the whole suite confirmed the attempt, it SHALL
  also carry that run's test counts, its coverage and whether it was reused. The entry's own test counts and coverage
  are then the confirmation's, as before;
- `text` and `reasoning`: the model's reply and, when the provider returns it, its reasoning. They SHALL be sent in
  chunks as they stream, no more than about every two seconds, with each chunk appended to the entry it continues;
- `stopped`: the task's work is over, with the stop reason, the last attempt that ran, the best coverage, and, for
  `budget`, the attempt not started. It SHALL be the last entry of a completed task;
- `resumed`: the agent took the task over after a restart, with the attempt it resumes at. Entries after it SHALL carry
  higher sequence numbers than every entry recorded before the restart.

The report's attempt lines SHALL carry the same scope and confirmation.

A text or reasoning entry SHALL be capped at 4 KB and marked truncated beyond it. Reporting SHALL NOT slow down or
fail the task: if an update cannot be sent, the agent SHALL go on and send later entries. None of this content SHALL
appear in the agent's logs, spans or metrics.

#### Scenario: A tool call is reported
- **WHEN** the model calls the run-tests tool in attempt 2 and the build succeeds with 12 passed, 1 failed at 72.1%
- **THEN** a `tool` entry for attempt 2 names the tool and carries build ok, 12 passed, 1 failed and 72.1%, and no source text

#### Scenario: A refused write is reported
- **WHEN** the model asks to write `src/Maf.Lab.Api/Program.cs`
- **THEN** a `tool` entry records the write as refused with its path and the reason, and the run continues

#### Scenario: A focused attempt confirmed on the whole suite
- **WHEN** attempt 2's measured run ran 58 tests from 3 related files, reached the target, and the whole suite then ran
  1219 tests green
- **THEN** the `attempt` entry carries scope `related`, 3 files and 58 tests, and a confirmation with 1219 passed. Its
  own test counts are the confirmation's

#### Scenario: The whole suite ran instead
- **WHEN** attempt 1 asked for the related tests but the runner ran the whole suite because nothing related was
  selected
- **THEN** the `attempt` entry carries scope `all` with that reason and no confirmation

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

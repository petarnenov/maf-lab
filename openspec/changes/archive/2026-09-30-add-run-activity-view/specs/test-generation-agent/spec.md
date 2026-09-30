## ADDED Requirements

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
  chunks as they stream, no more than about every two seconds, with each chunk appended to the entry it continues.

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

#### Scenario: No content in telemetry
- **WHEN** the agent's logs and spans for a run are inspected
- **THEN** none contains the model's text, its reasoning or a tool's path summary

# Spec Delta

## MODIFIED Requirements

### Requirement: Ctrl+C stops what a tool started
Every CLI tool and every make target SHALL stop on Ctrl+C (SIGINT) and on SIGTERM:
- at a safe point — between items, never half-way through a step that must be written whole;
- cancelling any server work it started (an admin job, a test-generation run) before it exits;
- ending with one line that says it was cancelled, what was done and what to run again, and exit code 130.

A tool SHALL handle SIGINT itself, on the same path as SIGTERM; a tool ended by the runtime's default reaction to
SIGINT has not stopped.

#### Scenario: Indexing interrupted
- **WHEN** a developer presses Ctrl+C during `make index`
- **THEN** the document being written is finished, no further document is started, the tool says how many documents
  were indexed and to run `make index` again, and it exits 130

#### Scenario: A script that started server work
- **WHEN** a developer presses Ctrl+C while a script waits on a coverage refresh it started
- **THEN** the script cancels that job before it exits, and the job ends canceled

#### Scenario: A .NET tool interrupted with Ctrl+C
- **WHEN** a developer presses Ctrl+C while `make eval`, `make index` or `make eval-a2a` waits on a service
- **THEN** the tool's own handler stops it, as SIGTERM would: it writes its "cancelled" line and exits 130, rather
  than being ended by the runtime with no line

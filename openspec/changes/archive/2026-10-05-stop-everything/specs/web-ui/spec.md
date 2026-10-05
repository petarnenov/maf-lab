# Spec Delta

## ADDED Requirements

### Requirement: Index administration can be stopped
On the index administration screen, an index run or a migration in progress SHALL stop on Esc (stop-anything): the
admin job is cancelled through its cancel route, the screen says "Stopping…" until the job reports canceled, then
shows it canceled with how far it got. A drift report still loading SHALL be aborted by Esc or by leaving the screen.

#### Scenario: Stopping an index run
- **WHEN** an administrator runs indexing and presses Esc while documents are being indexed
- **THEN** the job ends canceled after the document in hand, and the screen shows it canceled with the count reached

#### Scenario: Stopping a migration
- **WHEN** an administrator runs a migration and presses Esc
- **THEN** the migration stops after its current batch and ends canceled; running it again continues from there

### Requirement: Every request the web makes can be aborted
Every request the web sends to the api SHALL carry an abort signal: the one react-query gives its query or mutation,
or the page's own for work it started. Aborting it SHALL end the request, and the server SHALL stop the work it was
doing for it. Slow reads — the drift report, the topology probe, the compliance chain check and export, code
snippets — SHALL stop on Esc and when the page is left.

#### Scenario: Leaving the topology screen mid-probe
- **WHEN** the topology probe is running and the person navigates away
- **THEN** the request is aborted, and the api stops probing

# Spec Delta

## MODIFIED Requirements

### Requirement: Every request the web makes can be aborted
Every query the web sends to the api SHALL carry the abort signal react-query gives it, and a page's own long request
(the compliance export) its own; aborting it SHALL end the request, and the server SHALL stop the work it was doing
for it. A mutation SHALL NOT be aborted: the mutations that start work answer at once with that work's id, and the
work SHALL be stopped by that id through its own cancel route; aborting the mutation could leave the work running
with no id to stop it by. While a mutation is in flight, the control that sent it SHALL NOT send it again. Slow
reads — the drift report, the topology probe, the compliance chain check and export, code snippets, and the reports of
the telemetry, Jev statistics, evals and A2A admin screens — SHALL stop on Esc and when the page is left, and SHALL say
"Esc to stop" while they load.

#### Scenario: Leaving the topology screen mid-probe
- **WHEN** the topology probe is running and the person navigates away
- **THEN** the request is aborted, and the api stops probing

#### Scenario: Esc on a report still loading
- **WHEN** the telemetry, Jev statistics, evals or A2A admin screen is still loading its report and the person
  presses Esc
- **THEN** the request is aborted, and the screen keeps what it last showed

#### Scenario: Starting work is not aborted
- **WHEN** a person starts a test-generation run and presses Esc while the request that starts it is still in flight
- **THEN** the request is not aborted; once it answers with the run's id, Esc stops the run through its cancel route

# Spec Delta

## MODIFIED Requirements

### Requirement: The browser is proved against runs the server produced
Runs captured from the running stack SHALL be held in `evals/ui-events.jsonl`, each with the state the browser
should end in, and replaying one SHALL produce that state. A change to what the server emits SHALL therefore be
visible as a failure in the browser's own tests.

A recorded run SHALL carry the moment it was captured, and SHALL be replayed as of that moment. A recording
carries real timestamps — the expiry of a proposal above all — so replaying it against the clock of the day it is
run would make a recording decay into a failure that says nothing about the code.

#### Scenario: A recorded run
- **WHEN** a recorded run is replayed through the reducer
- **THEN** the answer, the tool calls, the sources and the pending write match what the recording says

#### Scenario: A run that paused
- **WHEN** the recorded run is one that paused for a confirmation
- **THEN** replaying it leaves the turn waiting on that proposal

#### Scenario: A recording that has been kept a while
- **WHEN** a run recorded long enough ago that its proposal's expiry has passed is replayed
- **THEN** it still produces the state it recorded, because it is replayed as of when it was captured

# Spec Delta

## ADDED Requirements

### Requirement: A run can be rejoined from any replica
While a run is in progress its state SHALL be readable by every replica: the answer as it stands, the tool calls
made and how each ended, whether it is waiting for a person, and — once it is over — how it ended. A caller that
lost its stream SHALL be able to ask for the run by its identifier through any replica and be given that state.

Asking about a run SHALL be subject to the same ownership as the thread it belongs to: a run of another
principal's thread SHALL be reported as not found. A run whose state is no longer kept SHALL be reported as not
found rather than as an empty run.

#### Scenario: The tab was closed
- **WHEN** a client abandons a run's stream and later asks for that run through another replica
- **THEN** it is told what the turn has said so far, which tools it called and how they ended

#### Scenario: A run that has finished
- **WHEN** a run has ended and is asked for
- **THEN** it reports that it ended and how, rather than appearing still to be running

#### Scenario: A run that stopped for a person
- **WHEN** a run ended waiting for an approval and is asked for
- **THEN** it says so, and names what is waiting

#### Scenario: Someone else's run
- **WHEN** a run of another principal's thread is asked for
- **THEN** it is reported as not found

#### Scenario: A run nobody kept
- **WHEN** a run older than the period its state is kept for is asked for
- **THEN** it is reported as not found

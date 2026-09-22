# Spec Delta

## ADDED Requirements

### Requirement: Telemetry screen
The `/telemetry` screen SHALL show what the stack has measured about itself, over a period the user chooses, and
SHALL be reachable from the main navigation.

It SHALL show: how many runs and turns were started and how many failed; how long a turn takes; how long a model
call takes and how many tokens it used, by model; tool calls by tool and outcome; how long retrieval takes, split
into its stages; and how those numbers are spread across the instances that served them. Each number SHALL say
which period it covers, and a number the stack has not measured yet SHALL read as no data rather than as zero.

The screen SHALL offer a way to open a turn's trace where the spans are kept, and a turn on the chat screen SHALL
offer the same for its own trace. When the metrics cannot be read, the screen SHALL say so and stay usable rather
than showing an empty chart.

No message content SHALL appear on the screen, because none of it is in the signals it reads.

#### Scenario: The stack's own numbers
- **WHEN** a signed-in user opens `/telemetry` after turns have run
- **THEN** the screen shows the turn and model numbers for the chosen period, each labelled with that period

#### Scenario: Nothing measured yet
- **WHEN** no turn has run in the chosen period
- **THEN** the screen says there is no data for it rather than showing zeros as a result

#### Scenario: Per instance
- **WHEN** two api replicas have served turns
- **THEN** the screen shows how the turns were spread between them

#### Scenario: From a turn to its trace
- **WHEN** a user opens the trace of a turn from the chat screen
- **THEN** that turn's spans open where the traces are kept

#### Scenario: Metrics unavailable
- **WHEN** the metrics store cannot be reached
- **THEN** the screen says the numbers are unavailable and still renders

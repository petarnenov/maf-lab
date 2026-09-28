# Spec Delta

## ADDED Requirements

### Requirement: Intent statistics screen
The `/admin/intents` screen SHALL show, for a period the user chooses, the intent classifier's statistics as charts,
and SHALL be reachable from the main navigation. Like the other admin screens it SHALL be shown only to FIRM_ADMIN.

It SHALL show: headline numbers (classified turns, share used, gated and failed, forced-retrieval rate, p90 latency);
a diagram of the classification pipeline with the configured floors and how many turns took each path; turns over time
by outcome; timeouts over time; Jev's choice against the intent the turn proceeded with; reasons an answer was not
used; the confidence histogram with the confidence floor marked; the in-domain histogram with the in-domain floor
marked; a confidence × in-domain scatter by outcome with both floors marked; the mean probability per intent; the
latency histogram with the timeout marked and the latency percentiles; the model versions that answered; and the
history of the `intent` eval (accuracy per language and per split, forcing rates, and the latest run's failures).

Each chart SHALL name its period, SHALL identify series by a legend or labels and not by colour alone, SHALL offer the
values behind the marks on hover, and SHALL read as no data rather than as zero when nothing was recorded. When the
statistics cannot be loaded the screen SHALL say so and stay usable. No message content SHALL appear on the screen.

#### Scenario: Charts after classified turns
- **WHEN** a FIRM_ADMIN opens `/admin/intents` after turns were classified
- **THEN** the headline numbers, the pipeline diagram and every chart are shown for the chosen period, with the floors
  drawn at 0.5, 0.2 and the timeout

#### Scenario: Nothing classified in the period
- **WHEN** no turn was classified in the chosen period
- **THEN** the screen says there is no data for it instead of drawing empty charts

#### Scenario: Statistics unavailable
- **WHEN** the statistics request fails
- **THEN** the screen shows an error and still renders its controls

#### Scenario: Intent eval history
- **WHEN** intent eval reports exist
- **THEN** the screen draws accuracy per language and per split over the runs and lists the latest run's failures

#### Scenario: Not an admin
- **WHEN** an ADVISOR opens `/admin/intents`
- **THEN** the screen shows access denied, as the other admin screens do

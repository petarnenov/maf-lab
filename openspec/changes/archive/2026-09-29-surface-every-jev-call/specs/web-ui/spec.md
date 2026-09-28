# Spec Delta

## ADDED Requirements

### Requirement: The retrieval view shows Jev's relevance judgment
For each search in a turn that asked Jev for a relevance judgment, the monitor's retrieval view SHALL show:
- Jev's model;
- the floor;
- the highest probability;
- whether the gate kept or silenced the search, or the reason Jev did not answer;
- whether Jev's answer ordered the results;
- how long the judgment took.

When the retrieval diagnostics carry each judged candidate's probability, the view SHALL list them by chunk. When
diagnostics were not requested, the view SHALL still show the judgment from the turn's `relevance` event.

The timeline SHALL show each `relevance` event as its own row with its duration.

#### Scenario: A judged search in the retrieval view
- **WHEN** a turn's search was judged by Jev and silenced by the gate
- **THEN** the retrieval view shows the model, the floor, the highest probability, "silenced", and each judged
  candidate's probability

#### Scenario: Judgment without diagnostics
- **WHEN** retrieval diagnostics were turned off and a search was judged
- **THEN** the retrieval view shows that search's judgment from its `relevance` event, and the timeline shows the
  `relevance` row with the judge's duration

## MODIFIED Requirements

### Requirement: Jev statistics screen
The `/admin/jev` screen SHALL show, for a period the user chooses, the statistics of every Jev call site in chat turns
as charts. It SHALL say that Jev calls made on the A2A partner path are not counted there.

The screen SHALL be reachable from the main navigation, and `/admin/intents` SHALL continue to reach it. Like the other
admin screens, it SHALL be shown only to FIRM_ADMIN.

It SHALL show a cross-cutting overview and a section per call site:

- **Overview:**
  - the total Jev requests over the period;
  - how many requests were unavailable;
  - the requests per classified turn;
  - total requests and unavailability over time, so a degraded Jev is visible across every site at once.
- **Intent:**
  - headline numbers: classified turns, share used, gated and failed, forced-retrieval rate, p90 latency;
  - a diagram of the classification pipeline, with the configured floors and how many turns took each path;
  - turns over time by outcome;
  - timeouts over time;
  - Jev's choice against the intent the turn proceeded with;
  - the reasons an answer was not used;
  - the confidence histogram with the confidence floor marked;
  - the in-domain histogram with the in-domain floor marked;
  - a confidence × in-domain scatter by outcome, with both floors marked;
  - the mean probability per intent;
  - the latency histogram with the timeout marked, and the latency percentiles;
  - the model versions that answered;
  - the history of the `intent` eval: accuracy per language and per split, forcing rates, and the latest run's
    failures.
- **Guardrail:**
  - how many prompts, tool results and reviewer texts were screened, and their decisions;
  - the question that tripped a block or a withholding;
  - the blocked, withheld and unscreened counts;
  - screenings over time, with the unscreened ones marked.
- **Relevance & rerank:**
  - how many searches Jev judged;
  - how many the gate silenced;
  - how many were reranked by Jev;
  - how many were left ungated because Jev was unavailable;
  - the distribution of each search's top relevance against the floor;
  - the judge latency.
- **Routing:**
  - how many data turns there were;
  - how many were routed, and to which tool;
  - why a data turn was not routed;
  - the model calls a turn made, routed against unrouted.

Each chart SHALL:
- name its period;
- identify series by a legend or labels, not by colour alone;
- offer the values behind the marks on hover;
- read as no data, not as zero, when nothing was recorded.

When the statistics cannot be loaded, the screen SHALL say so and stay usable. No message content SHALL appear on the
screen.

#### Scenario: Charts after classified turns
- **WHEN** a FIRM_ADMIN opens `/admin/jev` after turns were classified
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
- **THEN** the screen draws accuracy per language and per split over the runs, and lists the latest run's failures

#### Scenario: Not an admin
- **WHEN** an ADVISOR opens `/admin/jev`
- **THEN** the screen shows access denied, as the other admin screens do

#### Scenario: Every Jev call site has a section
- **WHEN** a FIRM_ADMIN opens `/admin/jev` after turns that screened prompts and tool results, gated and reranked
  searches, and routed data turns
- **THEN** the overview and the guardrail, relevance-and-rerank and routing sections are shown alongside the intent
  section, each with its own charts for the chosen period

#### Scenario: The scope is stated
- **WHEN** a FIRM_ADMIN opens `/admin/jev`
- **THEN** the screen says that it counts Jev calls made by chat turns, and that the A2A partner path is not counted

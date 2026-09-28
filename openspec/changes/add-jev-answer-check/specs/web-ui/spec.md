# Spec Delta

## ADDED Requirements

### Requirement: The monitor shows Jev's answer check
The monitor's timeline SHALL show a turn's `answer.check` event as a row of its own, in a colour of its own kind, with a
bar whose length is the check's latency and a title that names Jev, both probabilities against their floors and the
verdict (for example "Jev answer check: relevant 0.93 ≥ 0.50, grounded 0.41 < 0.50 — not grounded", or "Jev answer
check unavailable: <reason> — unchecked"). The event's data SHALL be readable in the row's details; no view of its own
is required. When the verdict is `not_relevant` or `not_grounded`, the monitor's header SHALL name it ("answer: not
grounded"); a pass or an unchecked answer SHALL add nothing to the header.

#### Scenario: A flagged answer in the monitor
- **WHEN** a turn whose answer Jev found not grounded is shown
- **THEN** the timeline has an `answer.check` row with its own colour and a bar for its latency, and the header says "answer: not grounded"

#### Scenario: A passing answer
- **WHEN** a turn whose answer passed the check is shown
- **THEN** the timeline has the `answer.check` row and the header has no answer chip

### Requirement: The Jev statistics screen shows the answer check
The `/admin/jev` screen SHALL list `answer` among the call sites of its overview with its requests, unavailable
requests and latency percentiles, and SHALL show an Answer check section: answers checked, the share not relevant and
not grounded with the floors, unchecked answers and how many of those Jev was unavailable for, and a latency histogram
against the check's timeout. A window with no answer check SHALL say so instead of drawing empty charts.

#### Scenario: Answer-check numbers on the screen
- **WHEN** the statistics report nine answer-check requests, one unavailable, and eight checked answers of which two were not grounded
- **THEN** the overview's `answer` row shows 9 and 1, and the Answer check section shows 25% not grounded and the latency histogram

#### Scenario: Nothing checked
- **WHEN** the window holds no answer check
- **THEN** the Answer check section says that no answer was checked

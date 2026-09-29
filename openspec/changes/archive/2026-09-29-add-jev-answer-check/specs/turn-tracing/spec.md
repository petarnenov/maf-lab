# Spec Delta

## ADDED Requirements

### Requirement: The answer check is traced
A turn whose answer was checked by Jev (as specified by `answer-check`) SHALL record one `answer.check` event, before
the turn's sources, signals and end. It SHALL carry the relevance and grounding probabilities, both floors, the
verdict (`pass`, `not_relevant`, `not_grounded`, `unchecked`), the versioned model, the reason when unchecked, how many
sources and characters were judged, and how many Jev requests the check made; the event's duration SHALL be the
check's latency, so the timeline draws it as a bar of its own. Its title SHALL name Jev, both probabilities against
their floors and the verdict, or, when unchecked, the reason. The event SHALL NOT contain the answer's text or the
sources' text. A turn that ran no check SHALL record no `answer.check` event.

#### Scenario: A checked answer in the trace
- **WHEN** a procedural turn is answered and Jev checks the answer
- **THEN** its trace holds an `answer.check` event after the model's response and before `sources`, `signals` and `turn.end`, with both probabilities, both floors, the verdict and a duration
- **AND** its title reads like "Jev answer check: relevant 0.95 ≥ 0.50, grounded 0.95 ≥ 0.50 — pass"

#### Scenario: The event holds no content
- **WHEN** a turn's answer is checked
- **THEN** the `answer.check` event contains neither the answer's text nor any excerpt's text

#### Scenario: Unchecked is recorded with its reason
- **WHEN** the answer check times out
- **THEN** the `answer.check` event's verdict is `unchecked` and its reason says it timed out

# Spec Delta

## MODIFIED Requirements

### Requirement: Feedback review queue
The `/admin/feedback` screen SHALL list turns flagged by production signals —
negative feedback, rephrased question, no tool on a how/why question, zero
retrieval results, long answer without sources — and provide a labeling form
whose submission appends a row to the matching eval dataset.

The zero-retrieval signal SHALL describe the turn, not one of its searches: it SHALL be raised when
a turn searched the documentation and finished with no sources at all. A turn that searched more
than once and ended with sources SHALL NOT carry it, whatever any single search returned along the
way. The queue is where a reviewer labels turns that went wrong, and a turn that answered from
documentation it cited has nothing to label.

#### Scenario: Zero-result turn flagged
- **WHEN** a turn searches the documentation and ends with no sources
- **THEN** the turn appears in the review queue with the signal "zero retrieval results"

#### Scenario: A turn that searched again and found something
- **WHEN** a turn's first search returns nothing and a later search returns documentation the answer cites
- **THEN** the turn does not carry the zero-retrieval signal

#### Scenario: A turn that never searched
- **WHEN** a turn answers without searching the documentation at all
- **THEN** it does not carry the zero-retrieval signal, whatever else it carries

#### Scenario: Label appended
- **WHEN** a reviewer submits a label for a flagged turn
- **THEN** a row is appended to the corresponding eval dataset

# Spec Delta

## ADDED Requirements

### Requirement: A relevance summary with every judged search
When a `search_documents` call asked the relevance judge, its result `_meta` SHALL carry a relevance summary. This
holds whether or not the trace flag was set.

The summary SHALL hold:
- whether the gate was on;
- the floor;
- how many candidates were judged;
- the highest probability;
- whether the gate silenced the search;
- the reranker in use, and whether the judge's answer actually ordered the results;
- the judge's model and duration;
- when the judge did not answer, the reason.

The summary MUST contain numbers and flags only: no query, no passage, no chunk or document id, and no probability
per candidate. It MUST NOT be part of the structured content or text content the model receives.

A call that did not ask the judge SHALL carry no summary. The full diagnostics SHALL remain available only on request.

#### Scenario: Judged without the trace flag
- **WHEN** the agent calls `search_documents` without the trace flag and the relevance gate is on
- **THEN** the result `_meta` carries the relevance summary and no diagnostics, and the structured content is identical
  to a call made with the flag

#### Scenario: The summary carries no content
- **WHEN** a judged search returns its relevance summary
- **THEN** the summary contains no query text, no passage text, no chunk or document id, and no probability per
  candidate

#### Scenario: Not judged
- **WHEN** a search runs with the gate off and a reranker other than the judge's
- **THEN** the result `_meta` carries no relevance summary

#### Scenario: Model never sees the summary
- **WHEN** the agent passes a judged search result to the model
- **THEN** the data envelope contains the structured content only, with no relevance summary

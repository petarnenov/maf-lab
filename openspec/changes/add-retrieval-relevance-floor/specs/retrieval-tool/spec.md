# Spec Delta

## ADDED Requirements

### Requirement: A search that finds nothing close returns nothing
When no candidate clears its branch's relevance floor, `search_documents` SHALL return an empty `results[]` with a
`refineHint` telling the caller how to ask better. It SHALL NOT fall back to the nearest candidates it happened to
find, and it SHALL NOT report the call as an error: finding nothing is an answer, not a failure.

An empty result SHALL be distinguishable by the caller from a failed call, because what follows differs — a caller
routes an empty result to whoever reviews questions the corpus cannot answer, and a failed call to whoever fixes
the store.

Diagnostics SHALL continue to report what the search found, whatever the floor then did with it. When diagnostics
are requested they SHALL include the floor applied to each branch and SHALL list the candidates that fell below
it, marked as such. Someone reading diagnostics SHALL be able to tell "the search found candidates, none of them
close enough" apart from "the search found nothing at all" — the two have different causes and different fixes.
The floor SHALL narrow what the model and the citations receive, never what diagnostics show.

#### Scenario: Nothing close enough
- **WHEN** a query's candidates all score below their branch floors
- **THEN** the tool returns no results, with a hint on how to rephrase, and the call is not marked as an error

#### Scenario: An empty result is not a failure
- **WHEN** a caller receives an empty result
- **THEN** it can tell that apart from an error result

#### Scenario: Diagnostics still show the near misses
- **WHEN** a traced search returns nothing because nothing cleared the floor
- **THEN** the diagnostics list the candidates that were found, mark those the floor dropped, and state the floor applied to each branch

#### Scenario: Diagnostics of a search with results
- **WHEN** a traced search returns results
- **THEN** the diagnostics state the floors that were applied, whether or not anything was dropped

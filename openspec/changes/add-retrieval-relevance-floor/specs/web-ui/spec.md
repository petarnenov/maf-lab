# Spec Delta

## ADDED Requirements

### Requirement: The retrieval view shows what the floor did
The retrieval view SHALL state the relevance floor applied to each candidate branch, alongside the other search
settings it already shows, so that an operator reading a search knows which floor produced it.

Where diagnostics report candidates that fell below a floor, the view SHALL show them, marked as dropped and
visibly apart from the candidates that were returned. It SHALL NOT hide them: a search that found near misses and
a search that found nothing at all look identical once the near misses are gone, and telling those two apart is
the reason to open this view.

A search that returned nothing SHALL say so, and say whether anything was found and dropped, rather than showing
empty lists.

#### Scenario: The floors are stated
- **WHEN** a user opens the retrieval view for any search
- **THEN** the view shows the floor applied to each branch

#### Scenario: Near misses are visible
- **WHEN** a search returned nothing because every candidate fell below its floor
- **THEN** the view lists those candidates with their scores, marked as dropped, and says the search returned nothing

#### Scenario: Nothing was found at all
- **WHEN** a search found no candidates before the floor was applied
- **THEN** the view says so, distinctly from a search whose candidates were all dropped

#### Scenario: Dropped candidates are not mistaken for results
- **WHEN** a search returned some results and dropped others
- **THEN** the returned and the dropped candidates are told apart on the screen

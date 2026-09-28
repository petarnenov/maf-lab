# Spec Delta

## MODIFIED Requirements

### Requirement: Retrieval diagnostics on request
When a `search_documents` request carries the `maf-lab/trace` flag in its `_meta`, the result SHALL include a
diagnostics object in the result `_meta` with:
- the serving replica and the tenant scope applied (tenant ids only);
- the effective settings: mode, fusion, dense vector, prefetch and final limits, rerank and the reranker selected,
  and whether the relevance gate is on;
- the BM25 query terms, each marked as being in the indexed vocabulary or not, with its IDF weight when it is in
  the vocabulary and no weight when it is not, and the dense model and dimensions;
- the dense-only, sparse-only and fused candidate lists (chunk id, doc id, tenant, score, rank), plus the rerank order
  when rerank is on;
- when a relevance judgment was requested: the relevance floor, each judged candidate's probability by chunk id, the
  highest probability, whether the gate silenced the search, the judge's model and duration, or — when the judge did
  not answer — the reason;
- the embedding, vector-store and relevance timings.

A term the indexed corpus has never seen has no IDF weight and cannot contribute to sparse matching. Diagnostics
SHALL report such a term rather than dropping it, and SHALL NOT invent a weight — neither zero nor any other
number — for it. A consumer SHALL be able to tell the two cases apart from the diagnostics alone.

Diagnostics MUST contain only candidates within the caller's tenant scope. They MUST NOT be part of the structured
content or text content the model receives. Without the flag, results SHALL NOT include diagnostics.

#### Scenario: Diagnostics requested
- **WHEN** the agent calls `search_documents` with the trace flag
- **THEN** the result `_meta` contains diagnostics whose candidates all belong to the caller's firm or shared, and the structured content is identical to a call without the flag

#### Scenario: A term the corpus has never seen
- **WHEN** a traced query contains a term that is not in the indexed BM25 vocabulary
- **THEN** the diagnostics list that term, mark it as outside the vocabulary, and give it no IDF weight

#### Scenario: A term the corpus knows
- **WHEN** a traced query contains a term that is in the indexed BM25 vocabulary
- **THEN** the diagnostics list that term, mark it as in the vocabulary, and give its IDF weight

#### Scenario: Not requested
- **WHEN** a client calls `search_documents` without the flag
- **THEN** the result has no diagnostics in `_meta`

#### Scenario: Model never sees diagnostics
- **WHEN** the agent passes a traced search result to the model
- **THEN** the data envelope contains the structured content only, with no diagnostics

#### Scenario: The relevance judgment is shown
- **WHEN** a traced search runs with the relevance gate on
- **THEN** the diagnostics give the floor, each judged candidate's probability, the highest probability and whether the search was silenced — or the reason the judge did not answer

### Requirement: A search that finds nothing close returns nothing
When no candidate clears its branch's relevance floor, or the relevance gate judges that no candidate addresses the
query, `search_documents` SHALL return an empty `results[]` with a `refineHint` telling the caller how to ask better.
It SHALL NOT fall back to the nearest candidates it happened to find, and it SHALL NOT report the call as an error:
finding nothing is an answer, not a failure.

An empty result SHALL be distinguishable by the caller from a failed call, because what follows differs — a caller
routes an empty result to whoever reviews questions the corpus cannot answer, and a failed call to whoever fixes
the store.

Diagnostics SHALL continue to report what the search found, whatever the floor or the gate then did with it. When
diagnostics are requested they SHALL include the floor applied to each branch and SHALL list the candidates that fell
below it, marked as such, and SHALL list the fused candidates a silenced search withheld. Someone reading diagnostics
SHALL be able to tell "the search found candidates, none of them close enough" apart from "the search found nothing at
all" — the two have different causes and different fixes. The floor and the gate SHALL narrow what the model and the
citations receive, never what diagnostics show.

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

#### Scenario: Silenced by the relevance gate
- **WHEN** the relevance gate judges that none of a query's fused candidates addresses it
- **THEN** the tool returns no results with the rephrase hint, the call is not an error, and traced diagnostics still list the fused candidates with their probabilities

# Spec Delta

## ADDED Requirements

### Requirement: Query language normalisation
A search query that is not written in the corpus's language SHALL be translated into it before the query is
embedded and before sparse encoding, so that dense and sparse retrieval both work on text drawn from the same
vocabulary as the indexed chunks. The corpus language and whether normalisation runs at all SHALL be configuration.

Normalisation SHALL preserve the meaning of the question and SHALL keep identifiers, codes and product terms as
they are written (for example `FS-REQUIRED`, a run id, a file name). It SHALL NOT change what the user is told:
only the text used to search is affected.

A query already in the corpus's language SHALL be used unchanged, without contacting a model. Translation that
fails, times out or returns nothing usable SHALL leave the original query in place, and the search SHALL proceed —
never fail — exactly as it does today. Results of the same translation MAY be reused within a run.

The tenant scope and the single tenant-scoped query path SHALL be unaffected: normalisation changes only the text
of the query, never who may see what.

#### Scenario: A question in another language finds the same documents
- **WHEN** a user asks "каква е процедурата когато липсва фий схема" over an English corpus
- **THEN** the search returns the same documents as the English question "what is the procedure when a fee schedule is missing"

#### Scenario: An English query is not translated
- **WHEN** the query is already in the corpus's language
- **THEN** no translation is attempted and the query is searched as written

#### Scenario: Identifiers survive
- **WHEN** a question in another language mentions `FS-REQUIRED` or run 4417
- **THEN** those tokens appear unchanged in the query that is searched

#### Scenario: Translation is unavailable
- **WHEN** the translation model fails or exceeds its budget
- **THEN** the original query is searched, the search still returns results, and the reason is recorded in the diagnostics

#### Scenario: Normalisation is switched off
- **WHEN** query normalisation is disabled by configuration
- **THEN** every query is searched exactly as written

#### Scenario: The monitor shows what was searched
- **WHEN** a query was normalised
- **THEN** the retrieval diagnostics carry both the original and the searched query

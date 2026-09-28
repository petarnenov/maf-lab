# Spec Delta

## ADDED Requirements

### Requirement: Provisioning a new dense embedding
Because a named vector cannot be added to an existing collection, the indexer SHALL provide an explicit rebuild
command that re-creates the chunk collection with every configured dense embedding, re-indexes the corpus, and fills
each configured dense embedding for every chunk. It SHALL run only when asked, SHALL say what it is about to discard,
and SHALL leave the collection searchable with the configured dense embedding when it finishes. It SHALL also be how an
embedding is removed: a rebuild provisions exactly the embeddings configured, and no others.

#### Scenario: A new embedding is added
- **WHEN** a dense embedding is added to the configuration and the rebuild command runs
- **THEN** the collection has a vector for every configured embedding, every chunk has all of them, and every chunk records the model versions it was embedded with

#### Scenario: Rebuild is never implicit
- **WHEN** the stack starts, or ordinary indexing runs, against a collection that lacks a configured embedding
- **THEN** nothing is deleted, and the error names the missing vector and the rebuild command

#### Scenario: The rebuilt index serves searches
- **WHEN** the rebuild finishes
- **THEN** a search for an answerable question through the running stack returns results

### Requirement: Every configured dense embedding is written and versioned
Indexing SHALL write every configured dense embedding for every chunk it writes, and SHALL record for each point which
model produced each of its dense vectors. A document SHALL count as unchanged only when its content, its update time
and the model of every configured dense embedding all match what is stored. Filling one embedding by migration SHALL
record that embedding's model and SHALL NOT change what is recorded for the others.

#### Scenario: A changed document keeps every embedding
- **WHEN** a document with two configured dense embeddings is edited and indexing runs
- **THEN** its new chunks carry both embeddings, and searching with either one finds them

#### Scenario: Migration leaves the others alone
- **WHEN** one embedding is filled by migration
- **THEN** only that embedding's recorded model changes, and the next indexing run skips every unchanged document


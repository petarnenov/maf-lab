# Spec Delta

## MODIFIED Requirements

### Requirement: Drift reporting
The indexer's `drift` command (`make drift`) and, while the index-admin plugin is installed, its admin endpoint for a
chosen corpus SHALL report the percentage of documents whose source updated_at is newer than their indexed updated_at.

The same report SHALL carry a graph section that compares the corpus's graph (the billing graph for billing's corpus)
with the same source documents, in the same tenant scope; for a corpus with no graph source, the graph section SHALL
say no graph is built for it. The graph section SHALL list:
- the source documents that have no document node;
- the document nodes built from content other than the source's current content, which includes nodes that do not
  record the content they were built from;
- the document nodes whose source document no longer exists.

It SHALL also give the number of source documents that are out of sync in the graph, as a count and as a
percentage of the source documents.

When the graph store cannot be reached, the graph section SHALL say it is unavailable, with no hostname, query text
or exception message, and the rest of the report SHALL be unchanged. The endpoint SHALL report only the tenants the
caller can read.

#### Scenario: Fresh index
- **WHEN** drift is requested immediately after a full index
- **THEN** the reported stale percentage is 0%

#### Scenario: Bumped source
- **WHEN** a source file's timestamp is bumped after indexing
- **THEN** the reported stale percentage is greater than 0% and the document is listed as stale

#### Scenario: Fresh graph
- **WHEN** drift is requested immediately after `make index` (which also builds the graph)
- **THEN** the graph section reports 0 documents out of sync and 0%

#### Scenario: Index refreshed, graph not
- **WHEN** a billing document's text changes and only the vector index is rebuilt
- **THEN** the index part lists nothing stale for it, and the graph section lists that document as behind

#### Scenario: New document not yet in the graph
- **WHEN** a billing document is added to the corpus and the graph has not been built since
- **THEN** the graph section lists its document id as missing from the graph

#### Scenario: Removed document still in the graph
- **WHEN** a billing document is deleted from the corpus and the graph has not been built since
- **THEN** the graph section lists its document id as no longer in the corpus

#### Scenario: Graph store down
- **WHEN** the graph store is stopped and drift is requested
- **THEN** the graph section says unavailable, and the stale percentage and lists of the index part are the same as
  with the graph store up

#### Scenario: Another firm's documents
- **WHEN** a firm A admin requests drift
- **THEN** neither part names a firm B document id

#### Scenario: A corpus without a graph
- **WHEN** an admin requests drift for a corpus whose manifest names no graph source
- **THEN** the index part is reported as usual and the graph section says no graph is built for the corpus

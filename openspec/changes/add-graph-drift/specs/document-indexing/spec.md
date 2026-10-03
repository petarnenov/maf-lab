## MODIFIED Requirements

### Requirement: Drift reporting
An admin endpoint and the indexer's `drift` command (`make drift`) SHALL report the percentage of documents whose
source updated_at is newer than their indexed updated_at.

The same report SHALL carry a graph section that compares the billing graph with the same source documents, in the
same tenant scope. The graph section SHALL list:
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

## ADDED Requirements

### Requirement: The drift command shows its progress
The indexer's `drift` command SHALL show a progress bar on stderr, following `progress-feedback`, and keep stdout for
the JSON report:
- an indeterminate bar with the current step while the corpus is read;
- then the tenants listed from the index, out of the total, with a percentage;
- then the graph step.

The bar SHALL end in one line that says whether the run succeeded, failed or was cancelled. That line SHALL give the
number of documents, the number stale in the index and the number out of sync in the graph, or say the graph was
unavailable, together with the elapsed time.

#### Scenario: Terminal run
- **WHEN** `make drift` runs in a terminal
- **THEN** a progress bar advances on stderr and ends in a line such as
  `✓ drift: done 624 documents in 0.9s — index 0 stale, graph 0 out of sync`, and stdout holds only the JSON report

#### Scenario: Graph store down
- **WHEN** `make drift` runs while the graph store is stopped
- **THEN** the final line says the graph was unavailable, the JSON report's graph section says unavailable, and the
  exit code is 0

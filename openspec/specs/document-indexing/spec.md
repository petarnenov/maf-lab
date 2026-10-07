# document-indexing Specification

## Purpose
Turns the sample corpus (Markdown docs, procedures, code, per firm and shared)
into tenant-tagged, searchable chunks, and keeps the index fresh, duplicate
free, and migratable between embedding models.

## Requirements

### Requirement: Source-type-aware chunking
The indexer SHALL chunk each source type by its natural structure: Markdown
documents by heading hierarchy (recording the heading path), procedures by
numbered step or section, and code by function or class (recording file path
and symbol name).

#### Scenario: Markdown heading path
- **WHEN** a Markdown file with nested headings "Billing > Fee schedules > Missing schedule" is indexed
- **THEN** the chunk under that heading has section_path "Billing > Fee schedules > Missing schedule"

#### Scenario: Procedure steps
- **WHEN** a procedure with numbered steps 1–5 is indexed
- **THEN** chunks align to step or section boundaries and no chunk splits a step mid-way unless the step exceeds the maximum chunk size

#### Scenario: Code symbols
- **WHEN** a code file containing two functions is indexed
- **THEN** each function is its own chunk carrying the file path and symbol name

### Requirement: Uniform chunk metadata
Every chunk SHALL carry doc_id (stable across runs), chunk_id, tenant_id (a tenant id or "shared"), source_type,
source_path, section_path, updated_at, model_version, and its text.

#### Scenario: Metadata complete
- **WHEN** any chunk is read back from the index
- **THEN** all listed fields are present and non-empty (section_path may be empty only for code files without symbols)

#### Scenario: Stable doc_id
- **WHEN** the same unchanged corpus is indexed twice
- **THEN** each document has the same doc_id and chunk_ids in both runs

### Requirement: Documents without a tenant are rejected
A document whose tenant cannot be determined MUST be rejected and reported,
never defaulted to "shared".

#### Scenario: Unowned document
- **WHEN** the indexer encounters a document outside any firm or shared folder
- **THEN** it skips the document, reports it as rejected, and indexes nothing for it

### Requirement: Switchable contextual enrichment
When contextual retrieval is enabled, the indexer SHALL prepend to each chunk,
before embedding, a short model-generated sentence describing the chunk's place
in its document. The feature SHALL be switchable by configuration.

#### Scenario: Enabled
- **WHEN** contextual retrieval is on and a document is indexed
- **THEN** each stored chunk's embedded text begins with a generated context sentence

#### Scenario: Disabled
- **WHEN** contextual retrieval is off
- **THEN** chunks are embedded from their original text only

### Requirement: Dense and sparse representations
Each chunk SHALL be stored as a single point with a dense embedding and a
BM25 sparse vector as named vectors. The BM25 vocabulary and IDF statistics
SHALL be derived from the indexed corpus and persisted.

#### Scenario: Both vectors present
- **WHEN** a chunk is indexed
- **THEN** its point has a non-empty dense vector and a non-empty sparse vector

### Requirement: Idempotent re-indexing
Re-indexing a changed document SHALL remove all of its previous chunks before
storing the new ones, so exactly one version of each document exists in the
index.

#### Scenario: Changed document
- **WHEN** a document is edited (sections added and removed) and re-indexed
- **THEN** the index contains only chunks of the new version and no chunk of the old version

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

### Requirement: Restartable embedding-model migration
A migration command SHALL add embeddings from a new model alongside the
existing ones, filling only chunks whose model_version is old. It MUST be
idempotent and restartable, and queries MUST keep succeeding while it runs.
Configuration SHALL select which dense embedding queries use.

#### Scenario: Interrupted migration
- **WHEN** the migration is killed midway and started again
- **THEN** it completes, every chunk carries the new model_version exactly once, and no duplicate points exist

#### Scenario: Queries during migration
- **WHEN** searches run while the migration is in progress
- **THEN** every search succeeds using the currently selected dense embedding

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

### Requirement: One corpus and collection per domain
The indexer SHALL index a domain's corpus into that domain's own collection and BM25 vocabulary, chosen by
configuration. `make index` SHALL index the billing corpus (`data/` → `maf_chunks`) and the portfolio corpus
(`data-portfolio/` → `maf_portfolio_chunks`). The same tenant layout rules SHALL apply to both. `make` SHALL index a
domain whose collection is empty.

#### Scenario: Portfolio corpus indexed on first start
- **WHEN** `make` runs and `maf_portfolio_chunks` is missing or empty
- **THEN** the portfolio corpus is indexed into it, and the billing collection is left as it is

### Requirement: The repository as a corpus
The indexer SHALL support a repository layout. In it, the corpus SHALL be the files version control tracks (and
untracked files it does not ignore) under configured include prefixes, minus configured exclude prefixes. Build output
and hidden paths SHALL be skipped, and so SHALL files larger than a configured size, which are reported as rejected.
Source files SHALL have source type `code` and Markdown SHALL have `docs`. Every document SHALL be shared. The tenant
SHALL come from the layout, which has exactly one tenant, and never from a path segment.

#### Scenario: Only included source is indexed
- **WHEN** the repository layout indexes with include `src/` and `web/src/`
- **THEN** files under `src/` and `web/src/` are indexed and files under `bin/`, `obj/` and `node_modules/` are not

#### Scenario: A generated file is rejected
- **WHEN** an included file is larger than the configured maximum
- **THEN** it is not indexed and appears among the rejected documents with the reason

### Requirement: Structural code chunks with their lines
In the repository layout, code SHALL be chunked per type and per member:
- A symbol's chunk SHALL include the doc comments, attributes and decorators written directly above it.
- Lines claimed by no symbol and holding more than braces SHALL form chunks of their innermost enclosing type, or of
  the file outside every type. This covers properties, fields, one-line records, imports and top-level statements.
- A repository chunk's section path SHALL begin with the file's path from the repository root.
- Every chunk the indexer can place in its file SHALL record its 1-based first and last line.
- Chunks of the tenant-layout corpora (billing, portfolio) SHALL be cut exactly as before this change.

#### Scenario: A method keeps its doc comment
- **WHEN** a C# method has a `///` summary and an attribute directly above it
- **THEN** the method's chunk starts at the summary and includes the attribute

#### Scenario: A property is not lost
- **WHEN** a class declares an auto-property between two methods
- **THEN** the property is in a chunk of that class

#### Scenario: Billing chunk ids unchanged
- **WHEN** the billing corpus is chunked after this change
- **THEN** it yields the same chunk ids and texts as before

### Requirement: Chunks fit the embedding model's window
Each embedding profile SHALL state the model's input window in tokens, when known (2048 for embeddinggemma).
- Chunks of a corpus configured with a token budget SHALL be sized by an estimate that does not undercount the
  model's tokens.
- For every corpus, a chunk whose text with its section path exceeds the ceiling SHALL be split further. The ceiling
  is the smallest window among the configured models, less the document prefix and, with contextual retrieval, room
  for the context sentence.
- When a window is known, the indexer SHALL ask the embedding provider to refuse over-long input rather than cut it. A
  refused chunk SHALL fail the run and SHALL NOT be stored with a vector of truncated text.
- The indexer SHALL be able to report each chunk's estimated tokens and the corpus's distribution against the ceiling.

#### Scenario: A long method is split under the ceiling
- **WHEN** a single method's text is estimated above the ceiling
- **THEN** it is stored as several chunks, each within the ceiling

#### Scenario: Over-long input is refused, not cut
- **WHEN** a text longer than the model's window is sent for document embedding
- **THEN** the provider refuses it and the index run fails naming the window, instead of storing a vector of cut text

#### Scenario: Report
- **WHEN** an operator lists chunks with token estimates
- **THEN** each chunk shows its estimated tokens and line span, and a summary gives the median, p95 and maximum against the ceiling

### Requirement: The BM25 vocabulary records its tokenizer
The BM25 vocabulary SHALL record the tokenizer it was built with, and queries SHALL be tokenized with that same
tokenizer. A vocabulary saved without one SHALL be read as the `words` tokenizer. A corpus configured with the `code`
tokenizer SHALL index each identifier whole and split into its words. Switching a corpus's tokenizer SHALL start its
vocabulary over.

#### Scenario: Existing vocabularies unchanged
- **WHEN** the billing vocabulary saved before this change is loaded
- **THEN** it tokenizes queries with `words`, exactly as before

### Requirement: An unchanged corpus is indexed without writes
An index run over a corpus in which no document changed SHALL skip every document and SHALL write nothing to the
store: the BM25 model SHALL be saved only when its vocabulary, its statistics or its tokenizer changed, and payload
indexes SHALL be created only when the collection lacks them.

#### Scenario: Repeat run
- **WHEN** an index run follows another over the same, unchanged corpus
- **THEN** every document is reported unchanged, no chunk is written or deleted, and neither the BM25 model nor any payload index is written

#### Scenario: One document changed
- **WHEN** one document's text changed since the last run
- **THEN** only that document is re-embedded, and the BM25 model is saved when its statistics moved

### Requirement: Index runs survive slow embedding batches
Embedding requests SHALL use the timeout `Models:EmbeddingTimeoutSeconds`. When it is not set, the services keep
HttpClient's 100 s and the indexer SHALL use 900 s, so a batch of long chunks on a CPU-bound model does not cancel the
run.

#### Scenario: A slow batch
- **WHEN** one embedding batch of an index run takes longer than 100 s but less than the indexer's timeout
- **THEN** the run continues and the batch's chunks are written

#### Scenario: Configured timeout
- **WHEN** `Models:EmbeddingTimeoutSeconds` is set
- **THEN** the indexer uses that value instead of its default

### Requirement: The indexer shows its progress
The indexer's `index` and `rebuild` commands SHALL show a progress bar on stderr, keeping stdout for the JSON summary:
indeterminate with the current stage while the corpus is read, then the documents done of the total with a
percentage and the document being embedded, following `progress-feedback`. The bar SHALL end in one line that says
whether the run succeeded, failed or was cancelled, with the count reached and the elapsed time; a failure SHALL name
the exception type only. Informational logs SHALL NOT be written to the console while the bar is shown.

#### Scenario: Unchanged repeat
- **WHEN** `make index` runs over unchanged corpora
- **THEN** each corpus ends in a line such as `✓ index maf_chunks: done 624/624 in 1.3s — 0 indexed, 624 unchanged, 0 chunks written`

#### Scenario: A document is being embedded
- **WHEN** a changed document takes seconds to embed
- **THEN** the bar names that document and its elapsed time keeps moving

### Requirement: Unreachable infrastructure is named, not thrown
When the indexer cannot connect to Qdrant or to the embedding endpoint, it SHALL end its progress bar in the failure
line, then print one line on stderr naming the service that is unreachable and the address it tried, and telling the
developer to run `make infra`; it SHALL exit with code 1. It SHALL NOT print a stack trace or an unhandled-exception
report, and SHALL NOT abort the process (no core dump). The line SHALL NOT carry exception message text, document
content or secrets.

#### Scenario: Qdrant down
- **WHEN** `make index` would run the indexer while nothing listens on Qdrant's address
- **THEN** stderr ends with a line such as `✗ Qdrant is not reachable at localhost:6334 — run 'make infra' (or 'make') and try again` and the exit code is 1

#### Scenario: Embedding endpoint down
- **WHEN** Qdrant is reachable but the embedding endpoint refuses connections
- **THEN** the line names the embedding endpoint and its address, and the exit code is 1

#### Scenario: Any other failure
- **WHEN** the run fails for a reason other than an unreachable service
- **THEN** the behavior is unchanged: the bar's failure line names the exception type and the exit code is non-zero

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
  `✓ drift: done 4/4 in 0.9s — 624 documents: index 0 stale, graph 0 out of sync`, and stdout holds only the JSON report

#### Scenario: Graph store down
- **WHEN** `make drift` runs while the graph store is stopped
- **THEN** the final line says the graph was unavailable, the JSON report's graph section says unavailable, and the
  exit code is 0

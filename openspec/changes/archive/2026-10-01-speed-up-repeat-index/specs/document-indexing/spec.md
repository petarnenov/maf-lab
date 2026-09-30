# Spec Delta

## ADDED Requirements

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

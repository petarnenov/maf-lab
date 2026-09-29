# Spec Delta

## ADDED Requirements

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

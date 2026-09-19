# Spec Delta

## Purpose

Turns the sample corpus (Markdown docs, procedures, code, per firm and shared)
into tenant-tagged, searchable chunks, and keeps the index fresh, duplicate
free, and migratable between embedding models.

## ADDED Requirements

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
Every chunk SHALL carry doc_id (stable across runs), chunk_id, tenant_id (a
firm id or "shared"), source_type, source_path, section_path, updated_at,
model_version, and its text.

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
An admin endpoint SHALL report the percentage of documents whose source
updated_at is newer than their indexed updated_at.

#### Scenario: Fresh index
- **WHEN** drift is requested immediately after a full index
- **THEN** the reported stale percentage is 0%

#### Scenario: Bumped source
- **WHEN** a source file's timestamp is bumped after indexing
- **THEN** the reported stale percentage is greater than 0% and the document is listed as stale

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

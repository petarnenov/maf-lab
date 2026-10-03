# Spec Delta

## Purpose

Keeps search fast while long-running embedding work runs: query embeddings and batch (document) embeddings are served
by separate local model instances with separate CPU shares, so a batch can never queue in front of a search.

## ADDED Requirements

### Requirement: Query and batch embeddings run on separate instances
The stack SHALL run two local embedding instances serving the same embedding model: an interactive instance and a
batch instance. Embedding a search query SHALL use only the interactive instance. Embedding documents — indexing,
re-indexing, rebuilding the collection, migrating to a dense vector, and an indexing run started from the admin page —
SHALL use only the batch instance. The choice SHALL follow from the operation (query or documents), never from a
request, tool argument or model output. Both instances SHALL produce the same vectors for the same input, so a
document indexed through the batch instance is found by a query embedded through the interactive one.

#### Scenario: A search during indexing
- **WHEN** a full indexing run is embedding documents and a user searches
- **THEN** the search's query embedding is answered by the interactive instance without waiting for any indexing
  batch, and its latency stays within twice the latency measured with no indexing running

#### Scenario: Indexing never touches the interactive instance
- **WHEN** `make index` runs against the running stack
- **THEN** every document embedding request goes to the batch instance and none to the interactive instance

#### Scenario: Same vectors on both instances
- **WHEN** the same text is embedded as a document on the batch instance and on the interactive instance
- **THEN** the two vectors are equal within floating-point tolerance (cosine similarity ≥ 0.9999)

### Requirement: CPU isolation between the instances
Each instance SHALL be confined to its own fixed set of CPUs, the two sets SHALL NOT overlap, and each instance SHALL
compute with no more threads than its set holds. The split SHALL be configurable, with a default that gives the
interactive instance the smaller share. A batch at full load SHALL NOT take CPU time from the interactive instance.

#### Scenario: Batch at full load
- **WHEN** the batch instance is embedding continuously
- **THEN** its CPU use stays within its own CPU set, and the interactive instance's CPUs remain available to it

#### Scenario: Custom split
- **WHEN** the operator sets a different CPU split through the documented variables and restarts the stack
- **THEN** each instance runs on the CPUs given to it and with a matching thread count

### Requirement: Both instances are ready before use
At start, the embedding model SHALL be pulled once into storage both instances share, and SHALL be loaded and kept
loaded in each instance before the stack reports ready, so neither the first search nor the first batch pays a cold
model load.

#### Scenario: First search after start
- **WHEN** the stack has just come up and the first search runs
- **THEN** its query embedding is answered without a model load

#### Scenario: First batch after start
- **WHEN** the stack has just come up and the first indexing run starts
- **THEN** its first batch is embedded without a model load

### Requirement: Single-instance fallback
When no batch endpoint is configured, document embeddings SHALL go to the configured embedding endpoint, as they did
before this change, so a setup with one embedding instance keeps working without changes.

#### Scenario: Only one endpoint configured
- **WHEN** a process runs with an embedding endpoint and no batch endpoint
- **THEN** both query and document embeddings use that one endpoint and indexing succeeds

### Requirement: Model-free CI covers both instances
In CI mode both instances SHALL be replaced by the deterministic model stub, so the end-to-end run exercises the
routing without downloading a model.

#### Scenario: CI end-to-end
- **WHEN** the model-free end-to-end run indexes and searches
- **THEN** indexing reaches the batch stub, search reaches the interactive stub, and no model is downloaded

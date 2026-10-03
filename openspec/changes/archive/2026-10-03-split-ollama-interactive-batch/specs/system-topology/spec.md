# Spec Delta

## ADDED Requirements

### Requirement: Embedding provider instances by role
The topology entry for the model provider used for embeddings SHALL list its two instances, named by role
(`interactive` and `batch`), each with its own health, its endpoint and whether the embedding model is present on it.
The entry SHALL be `healthy` when both are, `degraded` when only one answers or one lacks the model, and `unreachable`
when neither answers. The entry stays one service in the report and one node in the drawing.

#### Scenario: Both instances up
- **WHEN** both embedding instances answer and have the model
- **THEN** the embeddings entry is `healthy` and lists `interactive` and `batch` as healthy instances

#### Scenario: Batch instance down
- **WHEN** the batch instance does not answer and the interactive one does
- **THEN** the embeddings entry is `degraded`, `interactive` is listed healthy, and `batch` is listed with the reason
  it is not

#### Scenario: Single-instance setup
- **WHEN** no batch endpoint is configured
- **THEN** the entry lists the one instance it can speak for and is not reported `degraded` for the missing batch
  instance

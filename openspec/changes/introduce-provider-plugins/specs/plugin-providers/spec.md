# Spec Delta

## ADDED Requirements

### Requirement: Exactly the providers the core needs
Exactly one `decision-engine` provider SHALL be installed. The chat agent SHALL use the `chat-model` provider named by
`MAF_CHAT_MODEL`, which SHALL be installed. Otherwise make and the api SHALL refuse to start and name the problem. The
core SHALL reach models only through `IChatClient` and `IEmbeddingGenerator`, and typed decisions only through
`IDecisionEngine`. Any engine SHALL pass the same contract suite. A provider change SHALL reach stage or prod only
after the eval baselines hold with it.

#### Scenario: Two decision engines
- **WHEN** both `jev` and a local engine are installed
- **THEN** make refuses to start and names both

#### Scenario: The core minimum
- **WHEN** `make core` runs with the default `MAF_CORE_PROVIDERS`
- **THEN** exactly `jev`, `ollama-cloud` and `ollama-embeddings` are installed, and no domain plugin is

### Requirement: Embeddings keep their instance threads
Every embeddings request SHALL go to the instance its purpose names (queries or document batches), and SHALL carry
that instance's `num_thread`.

#### Scenario: A document batch
- **WHEN** the indexer embeds a batch through the `ollama-embeddings` provider
- **THEN** the request goes to `ollama-batch` and carries its `num_thread`

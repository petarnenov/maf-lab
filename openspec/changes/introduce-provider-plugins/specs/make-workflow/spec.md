# Spec Delta

## ADDED Requirements

### Requirement: The core's minimum providers
`make core` SHALL install the provider plugins named by `MAF_CORE_PROVIDERS` (the core's minimum providers that
"Plugin targets" refers to), and no domain, app or dev plugin. The
default for dev is `jev ollama-cloud ollama-embeddings`, and stage and prod name their own. make SHALL fail before
starting anything when the list does not give exactly one decision engine and the chat provider named by
`MAF_CHAT_MODEL`.

#### Scenario: A wrong core list
- **WHEN** `MAF_CORE_PROVIDERS` names no decision engine
- **THEN** `make core` fails before starting anything and says a decision engine is missing

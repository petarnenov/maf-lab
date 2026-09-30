# Spec Delta

## ADDED Requirements

### Requirement: Index targets run the built indexer
`make index`, `index-portfolio`, `index-code`, `reindex`, `drift`, `rebuild-index` and `migrate` SHALL run the indexer
from its build output rather than through `dotnet run`, and SHALL build it first only when a source, project or build
file of the indexer or of a project it references is newer than that output.

#### Scenario: Nothing changed
- **WHEN** `make index` runs twice in a row with no source and no corpus file changed
- **THEN** the second run does not build the indexer and each corpus finishes in about a second

#### Scenario: Indexer source changed
- **WHEN** a file under `src/Maf.Lab.Indexing` or `src/Maf.Lab.Retrieval` changed since the last build
- **THEN** the target builds the indexer once before indexing

# Spec Delta

## MODIFIED Requirements

### Requirement: One corpus and collection per domain
The indexer SHALL index a domain's corpus into that domain's own collection and BM25 vocabulary, chosen by
configuration. `make index` SHALL index the billing corpus (`data/` → `maf_chunks`), the portfolio corpus
(`data-portfolio/` → `maf_portfolio_chunks`) and the Bulgarian history corpus
(`data-bulgarian-history/` → `maf_bulgarian_history_chunks`). The same tenant layout rules SHALL apply to all of them:
a corpus whose only tenant folder is `shared` is indexed entirely as the shared tenant, and nothing else about it is
special. `make` SHALL index a domain whose collection is empty.

#### Scenario: Portfolio corpus indexed on first start
- **WHEN** `make` runs and `maf_portfolio_chunks` is missing or empty
- **THEN** the portfolio corpus is indexed into it, and the billing collection is left as it is

#### Scenario: Bulgarian history corpus indexed on first start
- **WHEN** `make` runs and `maf_bulgarian_history_chunks` is missing or empty
- **THEN** the Bulgarian history corpus is indexed into it as the shared tenant, and the other collections are left as they are

#### Scenario: One domain re-indexed alone
- **WHEN** `make index-bulgarian-history` runs
- **THEN** only the Bulgarian history collection and vocabulary are written, with the indexer's progress bar, and an unchanged corpus writes nothing

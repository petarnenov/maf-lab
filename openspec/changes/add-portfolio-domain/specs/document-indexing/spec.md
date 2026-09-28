# Spec Delta

## ADDED Requirements

### Requirement: One corpus and collection per domain
The indexer SHALL index a domain's corpus into that domain's own collection and BM25 vocabulary, chosen by
configuration. `make index` SHALL index the billing corpus (`data/` → `maf_chunks`) and the portfolio corpus
(`data-portfolio/` → `maf_portfolio_chunks`). The same tenant layout rules SHALL apply to both. `make` SHALL index a
domain whose collection is empty.

#### Scenario: Portfolio corpus indexed on first start
- **WHEN** `make` runs and `maf_portfolio_chunks` is missing or empty
- **THEN** the portfolio corpus is indexed into it, and the billing collection is left as it is

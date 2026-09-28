# Spec Delta

## ADDED Requirements

### Requirement: Retrieval measured per domain
A retrieval eval row SHALL name its domain (`billing` when it names none). Each domain's rows SHALL be scored against
that domain's collection, the portfolio rows as the `portfolio-hybrid` variant with its own thresholds. The dataset
check SHALL verify each row's chunk ids against its own domain's corpus.

#### Scenario: Portfolio rows are not scored against billing
- **WHEN** the retrieval suite runs
- **THEN** the billing variants score only billing rows and `portfolio-hybrid` scores only portfolio rows

### Requirement: Review-queue labels stay in their domain
The review queue SHALL resolve each search's sources to chunk ids in that search's own domain collection. A retrieval
label SHALL record the domain of the search that found its chunks. A label whose chunks came from both domains SHALL
be refused with a message saying so.

#### Scenario: A portfolio search's chunks
- **WHEN** a reviewer labels the chunks a `search_portfolio_documents` call returned
- **THEN** the appended retrieval row carries `"domain": "portfolio"`

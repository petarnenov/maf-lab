## MODIFIED Requirements

### Requirement: Index administration screen
The `/admin/index` screen SHALL let an admin trigger indexing, view drift
percentage, view the distribution of model_version across chunks, and start
the embedding migration. It SHALL be available only to FIRM_ADMIN.

The drift card SHALL also show the graph's drift in the page's theme: how many of the source documents are out of
sync in the graph, or that the graph is unavailable.

#### Scenario: Non-admin
- **WHEN** an ADVISOR opens `/admin/index`
- **THEN** access is denied

#### Scenario: Graph in sync
- **WHEN** an admin opens `/admin/index` and the graph matches the corpus
- **THEN** the drift card reads "Graph: 0 of N out of sync" under the index drift

#### Scenario: Graph unavailable
- **WHEN** an admin opens `/admin/index` while the graph store is down
- **THEN** the drift card shows the index drift as before and reads "Graph: unavailable", with no error state for the
  card

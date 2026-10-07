# Spec Delta

## MODIFIED Requirements

### Requirement: Index administration screen
While the index-admin plugin is installed, the `/admin/index` screen SHALL let an admin choose a corpus among the
installed plugins' corpora, trigger its indexing, view its drift percentage, view the distribution of model_version
across its chunks, and start its embedding migration. It SHALL be available only to TENANT_ADMIN.

The drift card SHALL also show the graph's drift in the page's theme: how many of the source documents are out of
sync in the graph, that the graph is unavailable, or that no graph is built for the corpus.

#### Scenario: Non-admin
- **WHEN** a USER opens `/admin/index`
- **THEN** access is denied

#### Scenario: Graph in sync
- **WHEN** an admin opens `/admin/index` and the graph matches the corpus
- **THEN** the drift card reads "Graph: 0 of N out of sync" under the index drift

#### Scenario: Graph unavailable
- **WHEN** an admin opens `/admin/index` while the graph store is down
- **THEN** the drift card shows the index drift as before and reads "Graph: unavailable", with no error state for the
  card

#### Scenario: Plugin not installed
- **WHEN** the index-admin plugin is not installed
- **THEN** there is no `/admin/index` screen and no link to it

### Requirement: Index administration can be stopped
While the index-admin plugin is installed, on the index administration screen, an index run or a migration in progress
SHALL stop on Esc (stop-anything): the admin job is cancelled through its cancel route, the screen says "Stopping…"
until the job reports canceled, then shows it canceled with how far it got. A drift report still loading SHALL be
aborted by Esc or by leaving the screen.

#### Scenario: Stopping an index run
- **WHEN** an administrator runs indexing and presses Esc while documents are being indexed
- **THEN** the job ends canceled after the document in hand, and the screen shows it canceled with the count reached

#### Scenario: Stopping a migration
- **WHEN** an administrator runs a migration and presses Esc
- **THEN** the migration stops after its current batch and ends canceled; running it again continues from there

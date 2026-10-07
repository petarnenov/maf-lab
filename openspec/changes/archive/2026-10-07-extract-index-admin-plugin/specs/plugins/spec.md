# Spec Delta

## ADDED Requirements

### Requirement: Index admin is a plugin
`index-admin` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/index-admin/` is deleted and the stack is rebuilt
- **THEN** nothing of `index-admin` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: An index job with the plugin in use
- **WHEN** index-admin is in use and an admin starts a reindex
- **THEN** the job runs, reports progress and can be cancelled as before

#### Scenario: Removing the plugin during a job
- **WHEN** an index or migrate job is open and `make plugin-off NAME=index-admin` runs
- **THEN** the plugin is not removed while the job is open, or the job is cancelled through the admin job store first

### Requirement: A plugin declares its corpus
A plugin that owns a corpus SHALL declare it in an optional `[corpus]` table of its manifest: its path inside the
plugin's folder, its chunk and meta collections, its layout and, when it has one, its graph source. The admin index
SHALL offer only the corpora of installed plugins whose layout is `tenants`, and a run SHALL name one of them.

#### Scenario: Two corpora installed
- **WHEN** billing and portfolio are installed and an admin runs indexing for portfolio
- **THEN** only portfolio's corpus is read and only its collection is written, for the admin's tenant and the shared one

#### Scenario: An undeclared or uninstalled corpus
- **WHEN** a run names a corpus no installed plugin declares
- **THEN** the answer is 404, no job starts and no collection is touched

#### Scenario: The code corpus
- **WHEN** the code plugin is installed
- **THEN** its repository-layout corpus is not offered to a tenant admin

#### Scenario: A declared corpus that is absent
- **WHEN** a manifest declares a corpus path that does not exist and an admin runs it
- **THEN** the run says the corpus does not exist and removes nothing

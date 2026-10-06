# Spec Delta

## MODIFIED Requirements

### Requirement: Plain make starts everything
Running `make` with no target SHALL check prerequisites, then build and start the core and the installed plugins:
`MAF_PLUGINS`, by default every bundled plugin allowed in `MAF_ENV` except `_example`. It SHALL wait until every
service is healthy, index the installed domains' corpora when their indexes are empty, and print the entry URL. It
MUST exit non-zero if any of these steps fails. With the default set, the stack SHALL be the same as before plugins
existed.

#### Scenario: Fresh start
- **WHEN** a developer runs `make` on a machine with the prerequisites and no running stack
- **THEN** all services become healthy, the corpus is indexed, and `http://localhost:7171` is printed as the entry point

#### Scenario: Already running
- **WHEN** `make` is run again while the stack is up and indexed
- **THEN** it completes without rebuilding unchanged images or re-indexing, and prints the entry point

#### Scenario: Service does not become healthy
- **WHEN** a service is still unhealthy after the wait timeout
- **THEN** make prints which service is unhealthy with its recent logs and exits non-zero

### Requirement: Index the codebase
While the `code` plugin is installed, `make index-code` SHALL index the repository into the codebase collection, its
plugin folder's make targets (`plugins/code/plugin.mk`) providing it. Documents that did not change SHALL be skipped.
`make index`, `make graph` and `make reindex` SHALL include the codebase and its graph, and `make` SHALL index the
codebase and build its graph when its collection or its graph nodes are missing, as it does for the other corpora.
`make dev` SHALL run the codebase server locally while the plugin is in the set make resolves. With the plugin's
folder deleted, none of these SHALL touch the codebase.

#### Scenario: First start
- **WHEN** `make` runs with the `code` plugin installed and an empty codebase collection
- **THEN** the repository is indexed into it before the banner is printed

#### Scenario: Re-run
- **WHEN** `make index-code` runs again with no file changed
- **THEN** every document is reported unchanged and nothing is re-embedded

#### Scenario: Without the plugin
- **WHEN** the `code` plugin's folder is deleted and `make index` runs
- **THEN** the billing and portfolio corpora and the billing graph are indexed, and nothing of the codebase

## ADDED Requirements

### Requirement: Plugin targets
The Makefile SHALL offer these plugin targets:
- `make core`: start the core with no plugin other than the core's minimum providers, which
  `introduce-provider-plugins` defines (until then, none);
- `make plugins`: list every plugin with its kind, scope, environments, description, dependencies, and whether it is
  installed and healthy;
- `make plugin-on NAME=` and `make plugin-off NAME=`: change `MAF_PLUGINS` in `compose/.env`, written by rename, and
  apply it to the running stack, starting or stopping only that plugin's services;
- `make plugin-new NAME= KIND=`: scaffold a plugin.

It SHALL include every `plugins/*/plugin.mk`. Switching an in-process plugin SHALL say before it starts that the api
replicas will be restarted one at a time. While `plugin-off` with `STOP_WORK=1` waits for stopped work to become
terminal, it SHALL show an indeterminate bar with the items still open. Ctrl+C during that wait SHALL exit 130, and
the cancels already issued SHALL stay in place, because they live in the work's stores.

#### Scenario: Interrupted while switching
- **WHEN** Ctrl+C is pressed during `make plugin-on NAME=code`
- **THEN** it stops at a safe point, `compose/.env` holds either the old or the new set, it says which plugins are
  installed and what to run again, and it exits 130

#### Scenario: Progress while switching
- **WHEN** `make plugin-on NAME=code` starts the plugin's services
- **THEN** one progress bar covers build, start, the wait for them to be healthy and the lb reload

# Spec Delta

## ADDED Requirements

### Requirement: The assistant's A2A surface is a plugin
`a2a` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/a2a/` is deleted and the stack is rebuilt
- **THEN** nothing of `a2a` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: A partner question with the plugin in use
- **WHEN** a2a is in use and a partner asks for run 4417's status
- **THEN** the task completes in the 1.0 shape, as before

## MODIFIED Requirements

### Requirement: Installed per deployment, read at run time
`MAF_PLUGINS` SHALL decide which plugins are installed:
- unset, every bundled plugin allowed in `MAF_ENV` except `_example`, excluding plugins with missing dependencies
  and their transitive dependants;
- `none`, no plugin (other than the core providers, once `introduce-provider-plugins` defines them);
- otherwise a comma-separated list.

The selected set SHALL be expanded with dependencies, and make SHALL fail on a cycle or a missing selected plugin
before starting anything. The api, copilot-runtime and the lb SHALL read the installed set and the manifests at run
time. Installing or removing a plugin without a `server/` part SHALL NOT recreate or restart them.

#### Scenario: A remote plugin is switched off
- **WHEN** `make plugin-off NAME=code` is run on a running stack with a chat run streaming
- **THEN** the run finishes (each api replica drains before it restarts, since the plugin has a `server/` part), the code
  services stop, `/code/mcp` answers 404, its tools are not offered from the next turn, lb and copilot-runtime were
  not recreated, and the api replicas restarted one at a time

#### Scenario: A dependency is missing
- **WHEN** `MAF_PLUGINS` names a plugin whose dependency does not exist
- **THEN** make fails before starting anything and names the missing plugin

#### Scenario: A folder with a dependent inspector is removed
- **WHEN** a plugin's folder is removed and `MAF_PLUGINS` is unset
- **THEN** the automatic set excludes that plugin, its inspector and any transitive dependants; unrelated plugins
  remain usable and documentation checks pass

# Spec Delta

## MODIFIED Requirements

### Requirement: Installed per deployment, read at run time
`MAF_PLUGINS` SHALL decide which plugins are installed:
- unset, every bundled plugin allowed in `MAF_ENV` except `_example`;
- `none`, no domain, app or dev plugin: only the core's providers;
- otherwise a comma-separated list.

`MAF_CORE_PROVIDERS` (the core's minimum providers) SHALL be installed first, whatever `MAF_PLUGINS` says.

The set SHALL be expanded with dependencies, and make SHALL fail on a cycle or a missing plugin before starting
anything. The api, copilot-runtime and the lb SHALL read the installed set and the manifests at run time. Installing
or removing a plugin without a `server/` part SHALL NOT recreate or restart them.

#### Scenario: A remote plugin is switched off
- **WHEN** `make plugin-off NAME=code` is run on a running stack with a chat run streaming
- **THEN** the run finishes (each api replica drains before it restarts, since the plugin has a `server/` part), the code
  services stop, `/code/mcp` answers 404, its tools are not offered from the next turn, lb and copilot-runtime were
  not recreated, and the api replicas restarted one at a time

#### Scenario: A dependency is missing
- **WHEN** `MAF_PLUGINS` names a plugin whose dependency does not exist
- **THEN** make fails before starting anything and names the missing plugin

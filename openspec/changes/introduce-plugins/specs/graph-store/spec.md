# Spec Delta

## MODIFIED Requirements

### Requirement: Graph browser as a dev inspector
The graph database's own browser SHALL be offered by its plugin, `neo4j-browser` (allowed in dev and qa only), on
`http://localhost:7175`, bound to loopback only, so it is absent wherever the plugin is not installed, CI included.

#### Scenario: Inspector in development
- **WHEN** the stack runs with the `neo4j-browser` plugin installed
- **THEN** the graph browser answers on `http://localhost:7175` and is not reachable from other hosts

#### Scenario: No inspector in CI
- **WHEN** the stack runs with `CI_MODE=1`
- **THEN** no graph browser port is published

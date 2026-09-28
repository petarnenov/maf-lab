# Spec Delta

## ADDED Requirements

### Requirement: The balancer reads its configuration from a mounted directory
The balancer SHALL read its configuration from a mounted directory, not from a single-file mount. A configuration file
replaced on the host (by a git checkout or merge) SHALL be the one a reload reads.

#### Scenario: Configuration changed by a merge
- **WHEN** a merge replaces `compose/lb/nginx.conf` and `make up` reloads the balancer
- **THEN** the reload succeeds and the balancer serves the new configuration

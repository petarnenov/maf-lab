# Spec Delta

## ADDED Requirements

### Requirement: Topology screen
The `/topology` screen SHALL render the drawn diagram of the stack with its live state on top. Each node SHALL show
its health (healthy, degraded, unreachable) in a way that does not rely on colour alone, its replica instances, and
the facts reported for it; each edge SHALL be drawn as the diagram draws it. The screen SHALL be reachable from the
main navigation and open to any signed-in user.

The screen SHALL refresh the state while it is open, SHALL say when the state was last refreshed, and SHALL offer a
manual refresh. Selecting a node SHALL show its full reported detail. When the state cannot be loaded, the diagram
SHALL still be shown, with an error telling the user the state is unknown rather than an empty screen.

#### Scenario: Live state on the diagram
- **WHEN** a signed-in user opens `/topology` with the stack running
- **THEN** every node is drawn in its place and marked healthy, and api and the MCP server show their replicas

#### Scenario: A service goes down while the page is open
- **WHEN** a service stops and the page refreshes
- **THEN** that node changes to unreachable with its reason, and the rest of the diagram is unchanged

#### Scenario: Node detail
- **WHEN** the user selects a node
- **THEN** the screen shows everything reported for it, including instances, versions and facts

#### Scenario: State cannot be loaded
- **WHEN** the topology request fails
- **THEN** the diagram is still rendered, with an error saying the live state is unavailable

#### Scenario: Not signed in
- **WHEN** no persona is selected
- **THEN** the screen says a persona must be picked, as the other screens do

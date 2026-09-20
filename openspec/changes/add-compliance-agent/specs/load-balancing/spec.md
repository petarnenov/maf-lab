# Spec Delta

## MODIFIED Requirements

### Requirement: Path routing
The balancer SHALL route `/api/` and `/dev/` to the api pool, `/mcp` to the mcp-retrieval pool, the A2A surface
(`/a2a` and the agent card at its well-known path) to the api pool, the compliance agent's own A2A surface to the
compliance pool, and every other path to the web app (SPA fallback to `index.html`).

Each agent's surface SHALL be reachable through the one entry point, so that a caller needs no address other than
the balancer's.

#### Scenario: SPA deep link
- **WHEN** a browser requests `http://localhost:7171/admin/feedback`
- **THEN** the web app's `index.html` is returned

#### Scenario: Both agent cards are discoverable through the entry point
- **WHEN** each agent's card path is requested through the balancer
- **THEN** each returns that agent's own card

### Requirement: Balancing across replicas
The api, mcp-retrieval and compliance tiers SHALL each run at least two replicas, and the balancer SHALL
distribute requests across all healthy replicas. Each replica SHALL identify itself in an `X-Instance` response
header.

#### Scenario: Requests spread
- **WHEN** 20 consecutive `GET /api/me` requests are sent through the balancer
- **THEN** responses carry at least two distinct `X-Instance` values

#### Scenario: Replica failure
- **WHEN** one api replica is stopped
- **THEN** subsequent requests through the balancer still succeed, served by the remaining replica

#### Scenario: The compliance tier is balanced too
- **WHEN** repeated requests are made to the compliance agent through the balancer
- **THEN** more than one compliance replica answers

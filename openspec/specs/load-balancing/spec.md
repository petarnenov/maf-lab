# load-balancing Specification

## Purpose
Puts every user- and agent-facing endpoint of maf-lab behind one load-balanced entry point on host port 7171, so the
stateless tiers can run as multiple replicas without changing client behaviour.

## Requirements

### Requirement: Single entry point on port 7171
The stack SHALL expose the web UI, the API (including the dev token issuer and the SSE chat stream) and the MCP
endpoint through one HTTP entry point on host port 7171. The api, mcp-retrieval and web services MUST NOT be
published on any other host port.

#### Scenario: Everything reachable on 7171
- **WHEN** the stack is started with compose
- **THEN** `GET /` returns the web app, `GET /dev/users` returns personas, `POST /api/chat` streams SSE, and an MCP client can list tools at `/mcp`, all on `http://localhost:7171`

#### Scenario: Old ports closed
- **WHEN** a client connects to host ports 5080, 5090 or 5174
- **THEN** the connection is refused

### Requirement: Path routing
The balancer SHALL route `/api/` and `/dev/` to the api pool, `/mcp` to the mcp-retrieval pool, and every other path
to the web app (SPA fallback to `index.html`).

#### Scenario: SPA deep link
- **WHEN** a browser requests `http://localhost:7171/admin/feedback`
- **THEN** the web app's `index.html` is returned

### Requirement: Balancing across replicas
The api and mcp-retrieval tiers SHALL each run at least two replicas, and the balancer SHALL distribute requests
across all healthy replicas. Each replica SHALL identify itself in an `X-Instance` response header.

#### Scenario: Requests spread
- **WHEN** 20 consecutive `GET /api/me` requests are sent through the balancer
- **THEN** responses carry at least two distinct `X-Instance` values

#### Scenario: Replica failure
- **WHEN** one api replica is stopped
- **THEN** subsequent requests through the balancer still succeed, served by the remaining replica

### Requirement: Streaming and MCP through the balancer
SSE responses SHALL be relayed unbuffered, with each event delivered as it is produced and connections allowed to
stay open for at least 10 minutes. MCP requests SHALL work statelessly through the balancer, with no session
affinity. The agent host SHALL reach the MCP server through the balancer.

#### Scenario: Chat stream through the balancer
- **WHEN** a procedural question is posted to `/api/chat` on port 7171
- **THEN** the client receives `tool_call_started` before `tool_call_finished`, then `sources`, then `done`, as separate events while the turn is running

#### Scenario: Consecutive MCP calls hit different replicas
- **WHEN** an MCP client makes several tool calls through `/mcp`
- **THEN** all succeed even though they are served by different mcp-retrieval replicas

### Requirement: Cross-replica consistency
State that must outlive a single request SHALL be shared across replicas: conversations, feedback, review queue,
audit and admin jobs.

#### Scenario: Conversation continues on another replica
- **WHEN** two turns of one conversation are served by different api replicas
- **THEN** the second turn has the first turn in its history

#### Scenario: Admin job status from any replica
- **WHEN** an indexing job is started through the balancer and its status is polled repeatedly
- **THEN** every poll returns the job, whichever replica serves it, until it reports `succeeded` or `failed`

#### Scenario: No duplicate concurrent jobs
- **WHEN** a FIRM_ADMIN starts indexing twice while the first job is running
- **THEN** the second request returns the running job instead of starting another

### Requirement: Balancer health
The balancer SHALL expose a health endpoint that reports ready only when it is serving, and compose SHALL start it
after the api and web tiers are healthy.

#### Scenario: Health
- **WHEN** `GET /lb-health` is requested on port 7171
- **THEN** it returns 200

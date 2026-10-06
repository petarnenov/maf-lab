# Spec Delta

## MODIFIED Requirements

### Requirement: The inspectors start with the stack
Each inspector SHALL be its own plugin, allowed in dev and qa only: `a2a-inspector` (the A2A Inspector on
`http://localhost:7172`), `mcp-inspector` (the MCP Inspector on `http://localhost:7173`), `redis-insight` (Redis
Insight on `http://localhost:7174`) and `neo4j-browser` (Neo4j Browser on `http://localhost:7175`). When installed,
`make` and `make up` SHALL start it together with the rest of the stack and SHALL wait for it as for every other
service, and `make down`, `make ps`, `make logs` and `make restart` SHALL include it. `make plugin-on NAME=…` SHALL bring
back only that inspector, and `make core` SHALL run none of them. Each inspector SHALL come from its project's official
source at a pinned version (a commit or an image tag), never a moving branch or `latest`.

#### Scenario: Plain make
- **WHEN** a developer runs `make` with the inspector plugins installed
- **THEN** `http://localhost:7172` serves the A2A Inspector, `http://localhost:7173` the MCP Inspector,
  `http://localhost:7174` Redis Insight and `http://localhost:7175` Neo4j Browser, and the banner points at
  `make plugins`

#### Scenario: Down stops them
- **WHEN** `make down` runs
- **THEN** no inspector is left running

#### Scenario: Core only
- **WHEN** `make core` runs
- **THEN** no inspector container is created

#### Scenario: Not allowed in stage
- **WHEN** `MAF_ENV=stage make plugin-on NAME=redis-insight` runs
- **THEN** it is refused, naming `redis-insight`

### Requirement: The protocol inspectors open ready to use
The A2A Inspector SHALL open with the assistant's agent card URL filled in and bearer authentication selected with a
partner token that is valid when the page is opened; when the URL is changed to the compliance agent's card, the token
SHALL be replaced by one for that agent's audience. The MCP Inspector SHALL list the lab's MCP servers — the built-in
domains' and every installed MCP plugin's, as its server.json names it, re-read from the installed set at least every
30 seconds — each with a dev user's bearer token, so that connecting takes one action; the token SHALL be renewed while
the Inspector runs so a listed server never carries an expired one. No token or secret SHALL be written to the
repository, an image, or a host volume; the dev credentials come from compose's environment.

#### Scenario: A2A without typing
- **WHEN** a developer opens `http://localhost:7172` and presses Connect
- **THEN** the assistant's card loads and a message can be sent, with no field typed

#### Scenario: Compliance agent
- **WHEN** the developer changes the card URL to `http://localhost:7171/compliance/.well-known/agent-card.json`
- **THEN** the token field holds a token for the compliance agent's audience, and connecting succeeds

#### Scenario: MCP without typing
- **WHEN** a developer opens `http://localhost:7173`
- **THEN** the built-in domains' servers and the installed MCP plugins' servers are listed, and switching one on
  connects it

#### Scenario: Lab not reachable at start
- **WHEN** the dev token cannot be obtained
- **THEN** the inspectors still start; the servers are listed or the URL is filled, and a token can be entered by hand

### Requirement: CI runs without the inspectors
With `CI_MODE=1` the end-to-end plugin set (`CI_PLUGINS`, a positive list) SHALL exclude the inspector plugins, so
they are not built, pulled or started, and nothing in the CI or end-to-end runs SHALL depend on them.

#### Scenario: Model-free end-to-end
- **WHEN** `make ci-e2e` runs
- **THEN** no inspector container is created and the run's result is unaffected

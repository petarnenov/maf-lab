## Purpose

Gives developers interactive access to the lab's A2A agents, MCP servers and shared state in Redis — the official A2A
and MCP inspectors and Redis Insight — running with the stack, the protocol inspectors reaching every surface at the
same URLs the developer uses on the host.

## ADDED Requirements

### Requirement: The inspectors start with the stack
`make` and `make up` SHALL start the A2A Inspector on `http://localhost:7172`, the MCP Inspector on
`http://localhost:7173` and Redis Insight on `http://localhost:7174` together with the rest of the stack, and SHALL wait for them as for every other service.
`make down`, `make ps`, `make logs` and `make restart` SHALL include them. Each inspector SHALL come from its
project's official source at a pinned version (a commit or an image tag), never a moving branch or `latest`.

#### Scenario: Plain make
- **WHEN** a developer runs `make`
- **THEN** `http://localhost:7172` serves the A2A Inspector, `http://localhost:7173` the MCP Inspector and
  `http://localhost:7174` Redis Insight, and the banner lists the three URLs next to the lab's entry point

#### Scenario: Down stops them
- **WHEN** `make down` runs
- **THEN** neither inspector is left running

### Requirement: The inspectors reach the lab at the host's URLs
From inside either inspector, `http://localhost:7171` SHALL reach the load balancer, so a developer can enter the same
addresses they use in the browser — the agent cards at `http://localhost:7171/.well-known/agent-card.json` and
`http://localhost:7171/compliance/.well-known/agent-card.json` (whose advertised interface URLs are on
`localhost:7171`), and the MCP servers at `http://localhost:7171/mcp`, `/portfolio/mcp` and `/code/mcp`. This SHALL hold
on macOS (Docker Desktop) and Linux without host networking. The inspectors SHALL NOT bypass the balancer or its
authentication: a call without a valid bearer token is refused as it is from any other client.

#### Scenario: A2A card connects
- **WHEN** a developer enters `http://localhost:7171` in the A2A Inspector with a partner bearer token from `/a2a/token`
- **THEN** the card loads and a message sent to the agent gets an answer through `http://localhost:7171/a2a`

#### Scenario: MCP tools listed
- **WHEN** a developer connects the MCP Inspector to `http://localhost:7171/mcp` (Streamable HTTP) with a bearer token
  from `/dev/token`
- **THEN** the inspector lists the server's tools and can call one

#### Scenario: macOS
- **WHEN** the same steps run on macOS with Docker Desktop
- **THEN** they behave as on Linux

### Requirement: Redis Insight opens on the lab's Redis
Redis Insight SHALL open already connected to the lab's Redis, with no setup step, no licence or telemetry prompt, and
analytics off. It SHALL show the lab's keys with their type and remaining TTL, string values that hold JSON formatted,
hashes and sorted sets, and offer a command line and a live command profiler (`MONITOR`). Redis itself SHALL stay
unpublished on the host.

#### Scenario: First open
- **WHEN** a developer opens `http://localhost:7174` after `make`
- **THEN** a database for the lab's Redis is listed and opens to its keys, with no licence dialog

#### Scenario: A run's state is visible
- **WHEN** a run is in progress
- **THEN** its state key is listed with its TTL counting down, and its JSON value is shown formatted

#### Scenario: Redis stays internal
- **WHEN** a client connects to host port 6379
- **THEN** the connection is refused

### Requirement: The protocol inspectors open ready to use
The A2A Inspector SHALL open with the assistant's agent card URL filled in and bearer authentication selected with a
partner token that is valid when the page is opened; when the URL is changed to the compliance agent's card, the token
SHALL be replaced by one for that agent's audience. The MCP Inspector SHALL list the lab's billing, portfolio and
codebase MCP servers, each with a dev user's bearer token, so that connecting takes one action; the token SHALL be
renewed while the Inspector runs so a listed server never carries an expired one. No token or secret SHALL be written
to the repository, an image, or a host volume; the dev credentials come from compose's environment.

#### Scenario: A2A without typing
- **WHEN** a developer opens `http://localhost:7172` and presses Connect
- **THEN** the assistant's card loads and a message can be sent, with no field typed

#### Scenario: Compliance agent
- **WHEN** the developer changes the card URL to `http://localhost:7171/compliance/.well-known/agent-card.json`
- **THEN** the token field holds a token for the compliance agent's audience, and connecting succeeds

#### Scenario: MCP without typing
- **WHEN** a developer opens `http://localhost:7173`
- **THEN** "maf-lab billing", "maf-lab portfolio" and "maf-lab code" are listed, and switching one on connects it

#### Scenario: Lab not reachable at start
- **WHEN** the dev token cannot be obtained
- **THEN** the inspectors still start; the servers are listed or the URL is filled, and a token can be entered by hand

### Requirement: The inspectors are reachable only from this machine
The inspector ports SHALL be published on the host's loopback interface only. The application's own ports SHALL stay
as they are: the inspectors SHALL NOT publish any port of the api, MCP or web services.

#### Scenario: Not on the network
- **WHEN** another machine on the local network connects to the host's port 7172, 7173 or 7174
- **THEN** the connection is refused

### Requirement: CI runs without the inspectors
With `CI_MODE=1` the inspectors SHALL NOT be built, pulled or started, and nothing in the CI or end-to-end runs SHALL
depend on them.

#### Scenario: Model-free end-to-end
- **WHEN** `make ci-e2e` runs
- **THEN** no inspector container is created and the run's result is unaffected

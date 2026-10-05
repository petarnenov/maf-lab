# Spec Delta

## MODIFIED Requirements

### Requirement: Path routing
The balancer SHALL route `/api/` and `/dev/` to the api pool, `/mcp` to the mcp-retrieval pool while the billing
domain is served there, the A2A surface (`/a2a` and the agent card at its well-known path) to the api pool, the compliance
agent's own A2A surface to the compliance pool until its own plugin change moves it, and every other path to the web app (SPA fallback to `index.html`).

A plugin's routes SHALL come only from its own snippet in `conf.d`, which is present only while the plugin is
installed. The core configuration SHALL reserve the route shapes plugins use, with `return 404` for each: `/<name>/mcp`
for MCP servers and the A2A agent paths. So a route of a plugin that is not installed SHALL answer 404 and SHALL NOT
fall through to the web app.

Each agent's surface SHALL be reachable through the one entry point, so that a caller needs no address other than
the balancer's.

#### Scenario: SPA deep link
- **WHEN** a browser requests `http://localhost:7171/admin/feedback`
- **THEN** the web app's `index.html` is returned

#### Scenario: Both agent cards are discoverable through the entry point
- **WHEN** each agent's card path is requested through the balancer
- **THEN** each returns that agent's own card

#### Scenario: A plugin's route while it is not installed
- **WHEN** the code plugin is not installed and a client posts to `http://localhost:7171/code/mcp`
- **THEN** the balancer answers 404, and the request reaches neither an MCP server nor the web app

### Requirement: The codebase server behind the balancer
While the `code` plugin is installed, its snippet SHALL route `/code/mcp` to the codebase server's pool, served at that
server's `/mcp`. It SHALL not buffer the responses, since Streamable HTTP may answer with an event stream, and SHALL
allow a read timeout long enough for ask_codebase to wait on the chat model.

#### Scenario: Route
- **WHEN** the `code` plugin is installed and an authenticated MCP client posts to `http://localhost:7171/code/mcp`
- **THEN** the request reaches a codebase server replica and lists search_codebase and ask_codebase

## ADDED Requirements

### Requirement: The balancer starts with any set of plugins
The lb's own configuration SHALL hold only core upstreams and locations, and SHALL include each installed plugin's
snippets from `conf.d`. The lb SHALL depend only on core services. A plugin's snippet SHALL be added only after the
plugin's services are healthy, and removed before they stop, each followed by a graceful reload. The lb SHALL start and
serve the core routes with no plugin, with every plugin, and with any single plugin, and SHALL NOT be recreated when a
plugin is installed or removed.

#### Scenario: Reload keeps streams
- **WHEN** a plugin is installed while a chat answer is streaming through the lb
- **THEN** the stream completes, and the plugin's route is served after the reload

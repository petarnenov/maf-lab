# Spec Delta

## ADDED Requirements

### Requirement: The codebase server behind the balancer
The load balancer SHALL route `/code/mcp` to the codebase server's pool, served at that server's `/mcp`. It SHALL not
buffer the responses, since Streamable HTTP may answer with an event stream, and SHALL allow a read timeout long enough
for ask_codebase to wait on the chat model.

#### Scenario: Route
- **WHEN** an authenticated MCP client posts to `http://localhost:7171/code/mcp`
- **THEN** the request reaches a codebase server replica and lists search_codebase and ask_codebase

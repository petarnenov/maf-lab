# Spec Delta

## ADDED Requirements

### Requirement: The Bulgarian history server behind the balancer
The load balancer SHALL route `/bulgarian-history/mcp` to the Bulgarian history server's pool, served at that server's
`/mcp`, spreading requests over its replicas as it does for `/mcp` and `/portfolio/mcp`. It SHALL not buffer the
responses, since Streamable HTTP may answer with an event stream. The api SHALL reach the server only through the
balancer.

#### Scenario: Route
- **WHEN** an authenticated MCP client posts to `http://localhost:7171/bulgarian-history/mcp`
- **THEN** the request reaches a Bulgarian history server replica, it lists `search_bulgarian_history`, and the response names the replica that answered

#### Scenario: Documented route
- **WHEN** `make docs` runs
- **THEN** the generated lb-routes table lists `/bulgarian-history/mcp` served by `mcp-bulgarian-history` at `/mcp`

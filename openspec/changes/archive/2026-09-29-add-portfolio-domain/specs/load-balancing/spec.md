# Spec Delta

## ADDED Requirements

### Requirement: Portfolio MCP through the balancer
The balancer SHALL route `/portfolio/mcp` to the portfolio MCP pool, spreading requests over its replicas as it does
for `/mcp`. The api SHALL reach the portfolio server only through the balancer.

#### Scenario: Portfolio MCP reachable through the entry point
- **WHEN** an authenticated MCP client connects to `http://localhost:7171/portfolio/mcp`
- **THEN** it lists the portfolio tools, and the response names one of the portfolio replicas

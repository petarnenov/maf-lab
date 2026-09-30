# Proposal

## Why

`add-codebase-domain` added a third MCP server, `mcp-code`, at `/code/mcp`, but the topology report and
`docs/topology.drawio` were not updated. `/topology` does not show the service, its health or its tools, and the
README's claim that the drawing is "the same picture" as the stack is no longer true. The test that keeps the drawing
and the report in step passes only because both miss the same node.

## What Changes

- The topology report gains an `mcp-code` node, built like `mcp-portfolio`:
  - its replicas are discovered through the `mcp-code` compose service;
  - its endpoint comes from the agent's codebase server;
  - its tools come from the same single tools/list the report already makes;
  - a failed tools/list degrades the node.
- The node has these edges:
  - api → mcp-code (`/code/mcp via lb`);
  - mcp-code → qdrant (gRPC);
  - mcp-code → ollama embeddings (embed);
  - mcp-code → chat provider (`ask_codebase`);
  - mcp-code → otel collector (OTLP).
- `docs/topology.drawio` draws the node and the same edges, in the free cell above Qdrant.
- The `system-topology` spec lists every domain's MCP server, the compliance reviewer and the shared state store
  among the reported services. The report already includes the last two.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `system-topology`: the Topology report requirement names every domain's MCP server, with a scenario for it.

## Impact

- `src/Maf.Lab.Api/Topology/` (`TopologyOptions.CodeService`, `TopologyProbe`), `docs/topology.drawio`,
  `tests/Maf.Lab.Tests/TopologyTests.cs`.
- The README topology screenshot is re-taken.

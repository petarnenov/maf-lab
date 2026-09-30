# Tasks

## 1. Report

- [x] 1.1 Add `TopologyOptions.CodeService` (`mcp-code`) and an `mcp-code` node to `TopologyProbe`: replicas, the codebase server's endpoint from `Agent:Servers`, its tools from the shared tools/list, and degraded when that list fails or no codebase server is configured. Add it to `NodeIds` and add its edges. Verify: a topology test asserts the node, its tools fact and the api → mcp-code edge

## 2. Drawing

- [x] 2.1 Draw `mcp-code` in `docs/topology.drawio` at x=930 y=120 with the report's edges. Verify: `The_diagram_is_served_and_holds_exactly_the_reported_nodes` and `No_two_boxes_in_the_diagram_overlap` pass

## 3. Verification

- [x] 3.1 `make test-dotnet`, `make lint-dotnet` and `make test-web` pass
- [x] 3.2 Rebuild the stack. Verify: `/topology` shows `mcp-code` healthy with its tools, and `make screenshots SHOTS=topology` re-takes the README image
- [x] 3.3 `openspec validate add-mcp-code-to-topology --strict` passes

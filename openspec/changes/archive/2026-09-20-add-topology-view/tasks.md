# Tasks

## 1. The diagram

- [x] 1.1 Draw `docs/topology.drawio` as uncompressed mxGraph XML: one node per service (`lb`, `api`, `mcp`, `qdrant`, `ollama-embeddings`, `chat-provider`, `web`) with the service id as the `mxCell` id, plus the edges between them (browser→lb, lb→web/api/mcp, api→mcp, api→chat-provider, mcp→qdrant, mcp/api→ollama-embeddings); verify it opens in draw.io unchanged and the file is plain XML in `git diff`
- [x] 1.2 Serve the diagram to the web app as a static asset (build copy or mount, not `?raw`), so editing it and reloading the page is enough; verify the file is fetchable from the running stack at its documented path

## 2. Topology report

- [x] 2.1 Add the contracts (`TopologyReport`, `TopologyNode`, `TopologyInstance`, `TopologyEdge`, health enum) and `TopologyOptions` (`ApiService`, `McpService`, probe timeout 2 s, cache 5 s, `LoadBalancerHealthUrl`, `WebHealthUrl`); verify defaults bind from configuration in a test
- [x] 2.2 Implement `TopologyProbe`: replica discovery by DNS then `GET /health` per address (falling back to this instance when discovery is unavailable), lb `/lb-health`, web `/healthz`, MCP `tools/list` through the balancer, Qdrant collection info through the injected client, embeddings `GET /api/tags`, chat provider from configuration only (never the key); all probes concurrent, each bounded by the timeout, failures becoming `unreachable` with a reason; verify tests with stubbed probes for: every service healthy, one unreachable (report still returns, others unaffected), Qdrant missing its collection (`degraded`), a probe that hangs (report returns within the timeout), discovery unavailable (single instance, stated)
- [x] 2.3 `GET /api/topology` (authenticated, any role) returning the report with `generatedAt` and the cache window, cached for 5 s across requests; verify tests: 401 without a token, 200 for an ADVISOR, no API key anywhere in the payload, second call within the window does not re-probe
- [x] 2.4 Facts on the nodes: chunk count and collection for Qdrant, chat/embedding model names and whether chat is remote, tool count for MCP, versions where reported; verify tests assert each fact appears on its node

## 3. Web

- [x] 3.1 `web/src/topology/parseDiagram.ts`: mxGraph XML → nodes (id, label, x, y, w, h) and edges (source, target), ignoring what it does not understand and rejecting a compressed file with a clear error; verify unit tests against the committed diagram and against a compressed sample
- [x] 3.2 `TopologyDiagram.tsx`: render the parsed diagram as SVG with a `viewBox` from its extent, node state shown by colour **and** a symbol plus the state in text, replica chips, click to select; verify tests: nodes render at their positions, an unreachable node is marked without relying on colour, selection fires
- [x] 3.3 `TopologyPage.tsx` + route `/topology` + nav entry: query with 5 s `refetchInterval`, "last refreshed" and a manual refresh, node detail panel, persona-missing notice, and the diagram still rendered with an error banner when the report fails; verify tests for all five web-ui scenarios with a stubbed fetch
- [x] 3.4 DTOs in `web/src/api/types.ts` under a new section; verify `npm run build` type-checks

## 4. Drift guard, verification and docs

- [x] 4.1 Test that the diagram's node ids and the report's node ids are equal in both directions, naming what is missing, and that the file is uncompressed XML; verify it fails when a node is removed from the diagram
- [x] 4.2 In the running stack on :7171: open `/topology` with 2+2 replicas and confirm every node is healthy with both replicas named; `docker stop` one api replica and one mcp replica and confirm the loss shows (a stopped container leaves DNS, so the node drops to `1 address(es)`; mcp turns `degraded` because the path through the balancer fails — a replica that is up but silent is listed `unreachable`, covered by the unit tests); stop Qdrant and confirm `unreachable` with a reason while the rest is unchanged; restart everything. Run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`
- [x] 4.3 Update `docs/http-api.md` (the endpoint), README (the page, replacing the ASCII drawing with a pointer to it) and DECISIONS.md (drawing parsed not exported, DNS discovery over heartbeats, what is deliberately not probed); verify the sections exist

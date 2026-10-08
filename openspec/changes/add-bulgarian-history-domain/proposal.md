# Proposal

**Risk tier: HIGH** — it adds a service and a route to the compose/lb topology and a fourth domain Noul to the Jev
request that gates forcing and the out-of-scope refusal (docs/rules/openspec-models.md §2).

## Why

The lab has three domains, all about one firm's own data or the lab's own code. It has no domain that is public by
nature — knowledge every firm and every advisor may read in full. A corpus for one already sits in `main`
(`data-bulgarian-history/shared/docs`, ten articles on the history of Bulgaria, CC BY-SA 4.0), but nothing indexes it,
no server searches it, and Jev puts a question about it outside every domain, so the chat refuses it with the fixed
reply. Adding the domain exercises the one thing the other three cannot: a corpus that is **shared only**, served to
everyone through the same tenant-scoped query path, with no new way to reach Qdrant.

## What Changes

- **A fourth domain, `bulgarian-history`, with its own MCP server `mcp-bulgarian-history`** (`src/Maf.Lab.BulgarianHistory`),
  modelled on `mcp-portfolio` minus the store and the read tools:
  - one tool, `search_bulgarian_history`, read-only, the same result shape as `search_documents`;
  - the same protocol (Streamable HTTP, stateless), the same bearer token, the same retrieval core as a library;
  - its own collection and BM25 vocabulary, `maf_bulgarian_history_chunks` and `maf_bulgarian_history_meta`, pinned by
    the server itself;
  - two replicas behind the balancer at `/bulgarian-history/mcp`; port 5093 in `make dev`.
- **The corpus is indexed as `shared`, and only `shared`.** The corpus has one tenant folder, `shared/`, so every chunk
  carries `tenant_id = shared` and every principal — any firm, any advisor — reads the whole of it through
  `TenantScopedSearch.QueryAsync`, whose filter is `tenant_id ∈ [own firm, shared]`. No tenant filter is bypassed, no
  query path is added, and no advisor id takes part (none does today). `make index`, `make reindex` and
  `make index-bulgarian-history` index it; `make` indexes it when its collection is empty.
- **A fourth Jev Noul, `in_bulgarian_history`, in the same classification request.** The domain is described as the
  history of Bulgaria from antiquity to the present day, in any language. What it is **not** is spelled out
  descriptively, never as a label: how an account's AUM or market value changed from quarter to quarter; the user's
  past conversations with this assistant; the billing runs that ran before and their statuses; the commits and changes
  made to the lab's source code. Thresholds stay (gate 0.2, scope 0.5).
- **A Bulgarian-history question is answered from its search.** When `bulgarian-history` is the primary domain in scope,
  the turn forces `search_bulgarian_history` for any intent but chitchat, as the codebase does today; a forcing intent
  in scope for it and another domain forces both searches. Data routing never routes to it (it has no read tools).
- **System prompt `system.v6`** adds the domain, its tool, examples and scope: the history of Bulgaria is in scope,
  answered only from what the tool returns; when the corpus does not cover a question, the answer says so.
  `system.v5` stays for rollback.
- **The fixed out-of-scope reply** (English and Bulgarian) names the history of Bulgaria beside billing, portfolios and
  the lab's code.
- **Tests.** The server lists one read-only tool with no tenant or firm input and rejects a call without a token; the
  classifier carries `in_bulgarian_history` in the one request and puts a history question in scope; the forced search;
  the reply text; the corpus layout is exactly `{shared}` with no rejected document; and — against a real Qdrant — a
  firm-a advisor, a firm-b advisor and a firm-c user with different advisor ids get the **same** result for the same
  query. The two assembly-enumeration tests (`QueryPathEnumerationTests`, `GraphStoreTests`) scan
  `Maf.Lab.BulgarianHistory`, and `QueryPathEnumerationTests` also gains `Maf.Lab.Portfolio` and `Maf.Lab.CodeSearch`,
  which it misses today.
- **Topology, web, evals, docs.** The topology report and the draw.io diagram gain the node and its edges; the chat's
  tool label, the Domains view colour and the admin tool list know the new tool; the `domain` and `selection` datasets
  gain history cases in English, Bulgarian and Latin-script Bulgarian plus negative rows for the four descriptive
  exclusions; the eval host loads the server; README, project.md, docs-sync, trace-events and DECISIONS are updated.

Deliberately **not** in this change (recorded in the design): a separate guard or answer-check context for history
excerpts (they are document excerpts, the billing content context already describes exactly that), and a per-domain
rewrite of the Jev statistics' `DomainStats` (codebase is not counted there today either; a follow-up change).

## Capabilities

### New Capabilities

- `bulgarian-history-mcp`: the fourth domain's MCP server, its one search tool over a shared-only corpus in its own
  collection, and the guarantee that every principal gets the same result.

### Modified Capabilities

- `intent-classification`: a fourth domain question, `in_bulgarian_history`, with descriptive exclusions, in the same
  request; a history question forces its search for any intent but chitchat.
- `chat-agent`: the out-of-scope reply names the history of Bulgaria; the system prompt covers the fourth domain.
- `load-balancing`: `/bulgarian-history/mcp` routed to the new pool.
- `document-indexing`: a third corpus and collection per domain, indexed by `make index` and on first start.
- `system-topology`: the fourth domain's MCP server in the report, the diagram and the probe.
- `eval-harness`: Bulgarian history measured as a domain and in tool selection, with negative cases for the exclusions.

## Impact

- New: `src/Maf.Lab.BulgarianHistory/` (Program, `Tools/BulgarianHistorySearchTool.cs`, `bulgarian-history.json`,
  Dockerfile, launchSettings), `src/Maf.Lab.Domain/BulgarianHistory/BulgarianHistoryContracts.cs`
  (`BulgarianHistoryCollections`, `BulgarianHistoryTools`), `tests/Maf.Lab.Tests/BulgarianHistoryDomainTests.cs`,
  `tests/Maf.Lab.IntegrationTests/BulgarianHistoryAcceptanceTests.cs`, `src/Maf.Lab.Api/Prompts/system.v6.md`.
- `Maf.Lab.Api`: `Domains`, `JevIntentClassifier` (the Noul and its instructions), `ChatTurnRunner` (the forced search
  by primary domain), `OutOfScope`, `SystemPrompt.DefaultVersion`, `TopologyOptions`, `TopologyProbe`, `appsettings.json`.
- `Maf.Lab.Eval`: `DatasetLoader` (tool, category, domain expectation), `DomainSuite` (failure text), `EvalOptions`,
  `EvalAgentHost`; `evals/domain.jsonl`, `evals/selection.jsonl`.
- Infrastructure: `compose/docker-compose.yml` (service, api `Agent__Servers__2__*`, depends_on), `docker-compose.ci.yml`,
  `compose/lb/nginx.conf`, `compose/mcp-inspector/start.mjs`, `Makefile` (`BULGARIAN_HISTORY_ENV`, `index`, `reindex`,
  `index-bulgarian-history`, `EVAL_HOST`, `up` scaling), `scripts/index_if_empty.sh`, `scripts/dev.sh`,
  `.vscode/launch.json`, `maf-lab.sln`, `docs/topology.drawio`.
- Tests touched: `QueryPathEnumerationTests`, `GraphStoreTests`, `SystemPromptTests`, `OutOfScopeTests`,
  `TopologyTests`, `FakeJev` (an `in_bulgarian_history` answer), `tests/Shared/FakeTools.cs` (the tool offered),
  `tests/Maf.Lab.IntegrationTests` project reference to the new server.
- Web: `web/src/chat/toolLabels.ts`, `web/src/monitor/domainData.ts`, `web/src/admin/toolNames.ts`, the chat
  placeholder, and their tests.
- Jev: one more Noul in the existing request, same state; the system prompt and the tool set change, so the `intent`,
  `domain` and `selection` suites are rerun and compared with their baselines (they need `OLLAMA_API_KEY` and
  `JEV_MAF_LAB`; accepting a baseline is the owner's call).
- No package version moves.

## Principles

- SOLID: single responsibility — the new server owns one domain and one tool, and the retrieval core stays the one
  place that queries Qdrant; open/closed — the domain is added by a row in `Domains.All`/`SearchTool` and
  `DomainQuestionIds`, a `Domains.SearchTool` lookup replaces the codebase-specific forcing so a fifth domain needs no
  new branch; dependency inversion — the server takes the retrieval core, auth and telemetry through DI as a library,
  exactly as `mcp-portfolio` does.
- Standards: official MCP (Streamable HTTP, stateless, tool annotations and output schema), bearer tokens (RFC 6750)
  with JWT (RFC 7519), OpenTelemetry for the server's spans, ports-and-adapters with a composition root per host, and
  the project's existing tenant layout (`{tenant}/{sourceType}/…`, `shared` as a tenant) for the corpus.
- Own: nothing new of the project's own. The shared-only corpus reuses the `shared` tenant DECISIONS §2 and §53 record.

## Progress

- Terminal: `make index-bulgarian-history` (and the new line in `make index`/`make reindex`) runs the existing indexer,
  whose `ConsoleProgress` bar reports "index maf_bulgarian_history_chunks" per document batch; `make up` prints the
  balancer reload line with the new replica count.
- Page: a chat turn that searches the corpus shows the existing tool-call card ("Searching Bulgarian history…", then
  "Searched Bulgarian history") and the monitor's `tool.forced`/`tool.call`/`tool.result` events, as every search does.
  No new UI-started process is added.

## Stopping

- Key: Esc on the chat page; Ctrl+C or SIGTERM in the terminal running the indexer.
- Stop: Esc → CopilotKit's stop → the aborted AG-UI request → its `CancellationToken` into the MCP client, the server's
  `search_bulgarian_history` and the Qdrant query (the existing path every search uses). The indexer stops at the next
  batch boundary and exits 130, as `prove-ctrl-c-in-dotnet-clis` proved for every indexer command.
- Recorded in: the request itself — no work outlives its request; the indexer writes nothing after a stop.
- Shown: the chat page shows "Stopping…" until the run's own state says it stopped, as today; the indexer prints its
  cancelled line and the shell sees exit 130.

## Documentation impact

- `README.md`: the overview ("three domains" → four), the Mermaid diagram (an `mcp-bulgarian-history ×2` node), the
  replica sentence, the Domains paragraph (`in_bulgarian_history`), the inspector's server list, `make dev` ports and
  the VS Code compound launch; the generated `make-targets` and `lb-routes` blocks are rewritten by `make docs`.
- `openspec/project.md`: the containers list gains `mcp-bulgarian-history`; the generated repo-layout block gains the
  new project's `<Description>` via `make docs`. `openspec/config.yaml` follows from it.
- `.github/copilot-instructions.md`: the generated lb-routes block via `make docs`.
- `docs/docs-sync.toml`: no new `[layout]` line is needed (`data-bulgarian-history` is already there); unchanged unless
  `make docs-check` asks.
- `docs/trace-events.md`: the `domain` event's domain list names all four domains.
- `docs/topology.drawio`: the new node and its edges (the topology test enforces it).
- `DECISIONS.md`: a new section (§81) — the shared-only domain, the descriptive exclusions, the alternatives rejected
  (an unfiltered query method, a public flag in the one query method, a `domain` field in `maf_chunks`, an anonymous
  server, answering from model weights, a tool inside `mcp-retrieval`, a separate Jev request) and the eval results.
- `CLAUDE.md`: the entry-point line lists the balancer's pools; it gains `mcp-bulgarian-history x2`.

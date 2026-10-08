# Tasks

Risk tier HIGH (proposal.md): `[mech]` → Haiku, `[std]` → Sonnet, `[hard]` → Opus (xhigh where tenancy is touched), two
tries per model then one up (docs/rules/openspec-models.md §4–5). The Jev review checklist (docs/rules/jev-usage.md §7)
applies: this change adds a Noul to the classification request (task 3.4).

## 1. Domain contracts and the server

- [x] 1.1 `[mech]` `src/Maf.Lab.Domain/BulgarianHistory/BulgarianHistoryContracts.cs`: `BulgarianHistoryCollections`
  (`maf_bulgarian_history_chunks`, `maf_bulgarian_history_meta`) and `BulgarianHistoryTools.Search =
  "search_bulgarian_history"`, namespace `Maf.Lab.Domain.BulgarianHistory`. Verify: `dotnet build src/Maf.Lab.Domain`
  passes and `grep -rn "Domain.History" src/Maf.Lab.Domain/BulgarianHistory` is empty.
- [x] 1.2 `[std]` Project `src/Maf.Lab.BulgarianHistory` (design D2): `.csproj` with `<Description>` and the three
  project references, `Program.cs` (server name, telemetry name, pinned collection, retrieval core, dev JWT, bootstrap,
  stateless MCP at `/mcp` with authorization, `BuildApp(args, configure)`), `Tools/BulgarianHistorySearchTool.cs`,
  `bulgarian-history.json` (:5093), `Properties/launchSettings.json`, `Dockerfile`; add it to `maf-lab.sln`. Verify:
  `dotnet build maf-lab.sln` passes and `dotnet run --project src/Maf.Lab.BulgarianHistory` answers `GET /health` 200.
- [x] 1.3 `[std]` `tests/Maf.Lab.Tests/BulgarianHistoryDomainTests.cs`, server part, by `PortfolioDomainTests`: the
  server lists exactly `search_bulgarian_history`, read-only, with no `tenant`/`firm`/`user`/`advisor` in its input schema
  and an output schema; the description names `get_aum_history`, `search_billing_runs` and `search_codebase` as the
  tools for what it is not; an empty query is a tool error; a call without a token is 401. Add the project reference to
  `Maf.Lab.Tests.csproj`. Verify: `dotnet test tests/Maf.Lab.Tests --filter BulgarianHistoryDomainTests` passes.

## 2. Indexing the shared-only corpus

- [x] 2.1 `[mech]` Makefile: `BULGARIAN_HISTORY_ENV`, a line in `index` and `reindex`, target
  `index-bulgarian-history` with its `##` help text, `BULGARIAN_HISTORY_REPLICAS ?= 2` in `up` (scale and reload echo
  and the `help` variable list); `scripts/index_if_empty.sh` gains its `index_domain` line. Verify: `make -n index` and
  `make -n index-bulgarian-history` print the three env values and the collection names.
- [x] 2.2 `[std]` Corpus layout test in `BulgarianHistoryDomainTests` (or `CorpusLoaderTests`):
  `CorpusLoader.Load(<repo>/data-bulgarian-history)` yields ten documents, every tenant `shared`, `LayoutTenants ==
  {shared}` and no rejected document. Verify: the test passes.
- [x] 2.3 `[hard]` `tests/Maf.Lab.IntegrationTests/BulgarianHistoryAcceptanceTests.cs` with a fixture that indexes
  `data-bulgarian-history` into a fresh collection with the fake embedder (by `CorpusIndexFixture`): a firm-a advisor
  (`adv-a-1`), a firm-b advisor (`adv-b-1`) and a firm-c FIRM_ADMIN (no advisor ids) search "покръстването на
  българите" in every retrieval mode and the three `SearchDocumentsResult`s are equal (doc ids, section paths, snippets,
  order); every chunk's tenant is `shared`. Add the project reference to `Maf.Lab.IntegrationTests.csproj`. Verify:
  `dotnet test tests/Maf.Lab.IntegrationTests --filter BulgarianHistoryAcceptanceTests` passes (Docker needed).

## 3. The fourth domain in the api

- [x] 3.1 `[std]` `Domains.BulgarianHistory = "bulgarian-history"` in `All` and `SearchTool`; `Domains.SearchOnly =
  {Codebase, BulgarianHistory}`. `JevIntentClassifier`: `BulgarianHistoryQuestionId = "in_bulgarian_history"`,
  `BulgarianHistoryDomain` instructions with the descriptive exclusions (design D3, verbatim), a row in
  `DomainQuestionIds`. `FakeJev`: a `BulgarianHistory` func and `BulgarianHistoryWords` keyword default (EN/BG history
  words, 0 otherwise), answered for `in_bulgarian_history`. Verify: classifier tests in `BulgarianHistoryDomainTests` —
  the request body carries `in_bulgarian_history` and the exclusion text ("from one quarter to the next", "earlier
  conversations", "ran before", "commits"), and none of the words "AUM history", "chat history", "run history",
  "commit history"; a history question is in scope alone; below scope but above gate is in scope alone; an AUM-history
  question is portfolio only; Jev down gives no verdict. Existing suites (`PortfolioDomainTests`, `CodebaseDomainTests`,
  `IntentClassifierTests`) stay green.
- [x] 3.2 `[hard]` `ChatTurnRunner`: replace `CodebaseSearch` with `SearchOnlyDomainSearch` over `Domains.SearchOnly`
  (design D4), keep the code-route drop keyed on the codebase search, no change to `ForcedSearches` or
  `DataToolRouter`. Verify: `CodebaseDomainTests` forcing tests still pass; new tests — a history question with intent
  data/other/procedural forces `search_bulgarian_history`, chitchat forces nothing, a crossing procedural question with
  the codebase forces both searches, a data question in the domain alone has `RouteReason` "no read tool belongs to a
  domain in scope" and the forced search; an api test with `FakeToolSource { WithBulgarianHistory = true }` shows only
  the history server loaded (`RequestedDomains`), the search forced, and the `domain` event's `loaded` =
  `["bulgarian-history"]`.
- [x] 3.3 `[std]` `Prompts/system.v6.md` (design D5), `SystemPrompt.DefaultVersion = "system.v6"`, `OutOfScope` replies
  in both languages. Verify: `SystemPromptTests` — default is v6 naming `search_bulgarian_history` and "history of
  Bulgaria", v5 rolls back and does not name it; `OutOfScopeTests` — both replies name the history of Bulgaria.
- [x] 3.4 `[hard]` Jev review checklist (docs/rules/jev-usage.md §7) over the changed request: closed atomic Noul,
  positive polarity, same state, one request, exclusions as situations not labels, thresholds and fallback unchanged,
  pinned model. Record the checklist's answers in DECISIONS §81 (task 7.2). Verify: the DECISIONS section lists each
  checklist item with its answer.
- [x] 3.5 `[std]` `tests/Shared/FakeTools.cs`: `WithBulgarianHistory` offering `search_bulgarian_history` with origin
  (`bulgarian-history`, `maf-lab-bulgarian-history`) and an `OfferedDomains` entry. Verify: used by 3.2's api test.

## 4. Infrastructure and topology

- [x] 4.1 `[mech]` `compose/docker-compose.yml`: service `mcp-bulgarian-history` (2 replicas, `*app-env`, collection
  env, `depends_on` qdrant/ollama-warm), api `Agent__Servers__2__Domain/Endpoint`, api and lb `depends_on`;
  `compose/docker-compose.ci.yml`: `*ci-model-env` for it; `compose/lb/nginx.conf`: `mcp_bulgarian_history_pool` and
  `location = /bulgarian-history/mcp`; `compose/mcp-inspector/start.mjs`: the fourth catalog entry;
  `src/Maf.Lab.Api/appsettings.json`: the dev server entry; `scripts/dev.sh`: run on :5093 and the echo;
  `.vscode/launch.json`: a configuration and the compound. Verify: `docker compose -f compose/docker-compose.yml config
  -q` passes and `grep -c bulgarian-history compose/lb/nginx.conf` ≥ 3.
- [x] 4.2 `[std]` Topology: `TopologyOptions.BulgarianHistoryService`, `TopologyProbe` resolve + `DomainServerAsync` +
  `NodeIds` + four edges (design D7); `docs/topology.drawio` box and edges with no overlap. Verify: `TopologyTests` —
  a new test by `The_codebase_server_is_reported_with_its_replicas_and_tools` for `mcp-bulgarian-history` (domain fact,
  tool, edges to api, qdrant, ollama-embeddings, otel-collector, none to neo4j or chat-provider); the diagram/report
  parity and overlap tests pass.
- [x] 4.3 `[std]` Enumeration tests: `QueryPathEnumerationTests.ProductAssemblies` += `Maf.Lab.CodeSearch`,
  `Maf.Lab.Portfolio`, `Maf.Lab.BulgarianHistory`; `GraphStoreTests.ProductAssemblies` += `Maf.Lab.BulgarianHistory`.
  Verify: both suites pass with the wider scan (a finding in Portfolio or CodeSearch is fixed or recorded, never the
  test narrowed).

## 5. Evals

- [x] 5.1 `[std]` `DatasetLoader` (tool, `bulgarian-history` selection category, domain expectation), `Metrics.Domain`
  (`bulgarianHistoryRecall`, `notConfused`), `DomainSuite` failure text, `EvalOptions.BulgarianHistoryMcpEndpoint`,
  `EvalAgentHost` in-process server and `Servers` entry, `Maf.Lab.Eval.csproj` reference, Makefile `EVAL_HOST` and
  `eval` env. Verify: `EvalHarnessTests`/`CodebaseDomainTests` dataset tests pass and a new test asserts
  `IsDomainExpectation("bulgarian-history")` and `("billing+bulgarian-history")`, and
  `Metrics.Domain` reports `bulgarianHistoryRecall`.
- [x] 5.2 `[std]` `evals/domain.jsonl`: ~12 positive rows (EN/BG/bg-latn, design/holdout) and 8 negative rows, two per
  exclusion with the labels design D8 gives; `evals/selection.jsonl`: 5 rows expecting `search_bulgarian_history`,
  category `bulgarian-history`. Verify: `dotnet run --project src/Maf.Lab.Eval -- --check-datasets` (or the loader
  tests) accepts every row.
- [x] 5.3 `[hard]` With the stack up and `OLLAMA_API_KEY`/`JEV_MAF_LAB` set, run `make eval SUITE=intent`,
  `SUITE=domain` and `SUITE=selection` (as a subagent, by docs/rules/openspec-models.md), compare with the baselines,
  read the floor sweep for the history cases. Verify: the three reports exist under `evals/reports/` and their numbers
  are copied into DECISIONS §81; if a key is missing, the section says the suites were not run and why. The baseline
  moves only by `make eval-accept` on the owner's say-so.

## 6. Web

- [x] 6.1 `[std]` `toolLabels.ts` label, `domainData.ts` colour, `admin/toolNames.ts` entry, the chat placeholder;
  Vitest tests for the label and the colour. Verify: `make test-web` and `make lint-web` pass.

## 7. Documentation

- [x] 7.1 `[mech]` README (overview, Mermaid node, replica sentence, Domains paragraph, inspector list, `make dev` ports,
  VS Code compound), `openspec/project.md` containers list, `docs/trace-events.md` domain list, `CLAUDE.md` entry-point
  line; then `make docs` for the generated blocks (repo layout, make targets, lb routes, config.yaml). Verify: `make
  docs-check` passes.
- [x] 7.2 `[std]` `DECISIONS.md` §81 "A fourth, shared-only domain: the history of Bulgaria
  (add-bulgarian-history-domain, <date>)": why, the shared-only tenancy and the seven alternatives rejected (design
  D1), the descriptive exclusions and why no label, the one-rule forcing, no new guard/answer-check context (D6), the
  enumeration-test gap closed, the Jev review checklist answers (3.4) and the eval results (5.3). Verify: `make
  docs-check` passes and `make verify` is green against the running stack.

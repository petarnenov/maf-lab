# Design

## Context

See proposal.md — Why. What the approach rests on, as the code stands in `main`:

- **Tenancy is one filter in one place.** `Principal.ReadableTenants` is `[FirmId, TenantId.Shared]`
  (`src/Maf.Lab.Domain/Tenancy/Principal.cs`), `TenantFilter.For` is the only producer of tenant conditions and
  `TenantScopedSearch.QueryAsync(Principal, …)` the only reader of chunks (`src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs`).
  Advisor ids take part in no retrieval filter. A corpus whose every chunk is `shared` is therefore readable, whole, by
  every principal, with nothing added or bypassed. The codebase corpus already works this way (DECISIONS §53).
- **The corpus layout decides the tenant.** `CorpusLoader` takes the tenant from the first path segment (`firm-*` or
  `shared`) and rejects anything else; `LayoutTenants` reports the folders it saw. `data-bulgarian-history/` has one
  folder, `shared/docs/`, ten Markdown articles (435 KB), each with a title, source and revision header.
- **A domain is a server, a corpus, a collection** (DECISIONS §40). `mcp-portfolio` reuses the retrieval core as a
  library and pins its collection after every other configuration source (`src/Maf.Lab.Portfolio/Program.cs`).
- **Domains are mostly tables.** `Domains.All`/`SearchTool`, `JevIntentClassifier.DomainQuestionIds`,
  `DomainVerdict.From`, `ForcedSearches`, `SelectDomains`, `McpToolSource` and the trace all iterate maps. The places
  that still name a domain explicitly: `ChatTurnRunner.CodebaseSearch` (forces the codebase search by primary domain),
  `OutOfScope`, `Guardrail.ContextOf`/`JevGuardQuestions.ContentFor` (codebase vs billing), `JevAnswerCheck`
  (code vs billing context), `DomainStats`, `TopologyProbe`, `DatasetLoader`, `DomainSuite.Label`, the web colours and
  labels.
- **Jev reads literally** (DECISIONS §54 addendum): the first codebase wording pulled run-failure questions and
  complaints about the assistant into the codebase until the domain stated what it is not. The word "history" already
  means four other things in this system: `get_aum_history`, the chat history (`Maf.Lab.Domain.History`,
  `/api/conversations`), billing runs that ran before, and the code's commit history.

## Goals / Non-Goals

**Goals:**
- A history question gets a grounded answer from the corpus, for any user, with identical results across firms and
  advisors, and the trace shows it as a fourth domain like the other three.
- Nothing about tenancy changes: no new query path, no flag, no parameter.
- Adding a fifth read-only domain later is rows in tables, not new branches.

**Non-Goals:**
- No read tools, store or seed for the domain; the corpus is the domain.
- No new Jev request, no new guard or answer-check context (see Decisions).
- No change to the three thresholds (`MinInDomain` 0.2, `MinDomainScope` 0.5, `MinConfidence` 0.5) unless the domain
  suite's floor sweep says so; moving a threshold is its own change.
- `DomainStats` (Jev statistics) keeps its billing/portfolio/both/none fields; codebase is not counted there today
  either. A per-domain dictionary is a follow-up change.
- Retrieval eval rows for the history corpus (`retrieval.jsonl` per-domain scoring) are not added; the selection and
  domain suites measure what this change decides.

## Decisions

### D1. The corpus is shared, and that is the whole tenancy story
- The corpus stays at `data-bulgarian-history/shared/docs/*.md` and is indexed with the standard tenant layout
  (`Indexing__Layout` default), into `maf_bulgarian_history_chunks` / `maf_bulgarian_history_meta`. Every chunk's
  `tenant_id` is `shared`; `TenantFilter.For` matches it for every principal.
- Alternatives rejected, each because it breaks a rule a test enforces:
  - an "unfiltered" query method — CLAUDE.md ("one method builds Qdrant queries"), tenant-isolation ("no query path
    without this restriction"), caught by `QueryPathEnumerationTests`;
  - a `public` flag on `SearchRequest` or `TenantFilter` — adds a tenant dimension to the one method; the test forbids
    a `Tenant` property on `SearchRequest`;
  - a `domain` field in `maf_chunks` — rejected in DECISIONS §40 (re-index everything, a second filter dimension,
    billing IDF skewing history terms);
  - an anonymous server — tenant-isolation requires a token on every MCP server, and the query takes a principal;
  - answering from model weights — contradicts the prompt's "answer only from tool results" and jev-usage §4.3.
- The "same result for everyone" guarantee is tested against a real Qdrant (Testcontainers): the corpus indexed with
  the fake embedder, then three principals — firm-a advisor with `adv-a-1`, firm-b advisor with `adv-b-1`, firm-c
  FIRM_ADMIN with no advisor ids — search the same phrase and the result DTOs are compared for equality (doc ids,
  section paths, snippets, order). A unit test checks `CorpusLoader.Load("data-bulgarian-history")` yields
  `LayoutTenants == {shared}`, no rejected document and ten documents.

### D2. One server, one tool, the portfolio server as the template
- `src/Maf.Lab.BulgarianHistory/Program.cs`: `ServerName = "maf-lab-bulgarian-history"`, telemetry service name
  `maf-lab-mcp-bulgarian-history`, `AddJsonFile("bulgarian-history.json")`, the collection pinned after every other
  source from `BulgarianHistory:Collection` / `:MetaCollection` (defaults `BulgarianHistoryCollections.Chunks/Meta`),
  `AddMafRetrievalCore`, `AddDevJwtAuthentication`, `BootstrapService`, `AddMcpServer … WithHttpTransport(Stateless)
  … WithTools<BulgarianHistorySearchTool>()`, `MapMcp("/mcp").RequireAuthorization()`. `BuildApp(args, configure)` as
  the others, so tests and the eval host start it in-process.
- `Tools/BulgarianHistorySearchTool.cs` is `PortfolioSearchTool` with the names and the description changed:
  `[McpServerTool(Name = search_bulgarian_history, ReadOnly, Idempotent, Destructive = false, OpenWorld = false,
  UseStructuredContent, OutputSchemaType = typeof(SearchDocumentsResult))]`, parameters `query`, `sourceTypes`
  (docs only in practice), `maxResults`; `_meta` trace and relevance as the portfolio tool. Description, in the project's
  form: "Searches the shared documentation on the history of Bulgaria — states, rulers, wars, uprisings, the
  liberation and unification, culture and religion — and returns matching snippets with their source and section. It
  never returns a synthesized answer. Use when: the user asks who, when, what happened or why about Bulgaria's past.
  Do not use for: an account's AUM or market value over quarters — use get_aum_history; billing runs that ran before —
  use search_billing_runs; the lab's code and its changes — use search_codebase; billing or portfolio procedures —
  use search_documents or search_portfolio_documents. Pass a natural-language phrase as query."
- `src/Maf.Lab.Domain/BulgarianHistory/BulgarianHistoryContracts.cs`: `BulgarianHistoryCollections { Chunks =
  "maf_bulgarian_history_chunks", Meta = "maf_bulgarian_history_meta" }` and `BulgarianHistoryTools { Search =
  "search_bulgarian_history" }`. The namespace is `Maf.Lab.Domain.BulgarianHistory`, never `…History`, which is the
  chat history.
- The corpus is in Bulgarian, so the server also pins `Retrieval:CorpusLanguage = bg` and `Retrieval:NormalizeQueryLanguage =
  false` (overridable through `BulgarianHistory:*`): the core's translator rewrites every non-Latin query into the corpus
  language, which for the other servers is English; here a Cyrillic question is already in the corpus's language, and the
  translation "the conversion of the Bulgarians" found nothing in the Bulgarian BM25 vocabulary (the acceptance test caught
  it). The embedding model is multilingual, so an English question still finds its passages untranslated.
- `bulgarian-history.json`: `Urls http://localhost:5093`, the two collection names, the two retrieval settings. `Dockerfile`: the portfolio one
  with the project path changed. `.csproj` `<Description>`: "MCP server for the Bulgarian history domain:
  search_bulgarian_history over a shared-only corpus (own collection)" — `make docs` reads it into the repo layout.
- Sources, guard and answer check need no change: the tool returns `SearchDocumentsResult`, which `SourceRef.FromSearchItem`,
  the guard's excerpt reader and `ReadItem.FromSearchItem` already parse (`Domains.IsSearch` is a lookup over
  `Domains.SearchTool`).

### D3. The fourth Noul, in the existing request
Per docs/rules/jev-usage.md the request is listed in full:
- **State**: unchanged, `JevState(question)` → `{ "user_question": "<text>" }`. Nothing is added to the state; a
  domain description in the state moved other answers in planning (DECISIONS §40).
- **Question** `in_bulgarian_history`, type Noul, instructions a `JevDomainInstructions(Domain, Languages, Question)`
  like the other three:
  - `Domain`: "The history of Bulgaria from antiquity to the present day: its states and rulers, wars and treaties,
    uprisings and the liberation, the unification, its church, culture, language and script, and the people and
    events of its past. Not in it: how an account's assets under management or market value changed from one quarter
    to the next; the user's own earlier conversations with this assistant; the billing runs that ran before and how
    they ended; the commits and changes made to this lab's source code."
  - `Languages`: the shared note ("Questions may be in English or in Bulgarian, and Bulgarian is often written in
    Latin letters.").
  - `Question`: "Is `user_question` about something in `domain`?"
  - The exclusions are descriptions of situations, as rule §4.2 asks for boundary cases, and never the labels
    "AUM history", "chat history", "run history" or "commit history": a label containing "history" would pull the
    question toward the domain it is meant to keep out.
- **Why Jev** (§2, §5): a closed yes/no; needs language understanding ("покръстването", "Simeon", "Saedinenieto"
  share no keyword list); code acts on the probability; it rides in the request the turn already sends, so no round
  trip; a keyword list would miss Latin-script Bulgarian and an LLM call adds seconds.
- **Thresholds**: the existing ones. Gate `MinInDomain` 0.2 on the highest domain; scope `MinDomainScope` 0.5; the
  review band between them puts the most probable domain alone in scope. Risk is read-only: a wrong "in scope" offers
  one search whose results the relevance gate filters; a wrong "out" on the first turn gives the fixed reply, the same
  failure a missed billing question has today.
- **Fallback**: unchanged. No answer or a failed request means no verdict: every server is loaded, nothing is forced,
  nothing is refused. A missing answer to this Noul alone leaves the other three in `ReadDomains` (it already skips
  unanswered domains).
- **Model**: pinned `jev-1.13.0` by configuration, recorded per call as today; `model` and `usage` logged by the
  client.
- **Where it lands**: `JevIntentClassifier.BulgarianHistoryQuestionId = "in_bulgarian_history"`, `BulgarianHistoryDomain`
  instructions, one row in `DomainQuestionIds`; `FakeJev` answers it from `BulgarianHistory` (a func) or keyword rules
  (history words in EN/BG, zero otherwise) so no existing test becomes a crossing by accident.

### D4. Forcing by a rule, not a branch
- `ChatTurnRunner.CodebaseSearch(decision, tools)` becomes `SearchOnlyDomainSearch`: when the primary domain in scope
  is one of `Domains.SearchOnly` (`{Codebase, BulgarianHistory}` — the domains with a search and no read tools) and the
  intent is not chitchat and the tool is offered, force that domain's search. Same semantics for the codebase, one
  rule for both. The code-route interplay (a routed graph call drops the forced codebase search) stays as it is, keyed
  on the codebase search.
- `ForcedSearches` is unchanged: procedural/mixed force every in-scope domain's search, so a crossing with billing
  ("how was the fee computed in 1878?" will not happen, but a crossing with the codebase can: "where does the code
  index the history corpus?") searches both.
- `DataToolRouter` is unchanged: `ToolDomain` has no history tool, so a data intent in that domain alone yields
  "no read tool belongs to a domain in scope", and the forced search answers it.
- `Domains.BulgarianHistory = "bulgarian-history"`, appended to `All` (trace order) and `SearchTool`.

### D5. Prompt, refusal
- `Prompts/system.v6.md` = v5 plus: the first paragraph names four domains; a "Bulgarian history:" tools block with
  `search_bulgarian_history`; examples ("Кога е Съединението на България?" → search_bulgarian_history; "Who was
  Simeon I?" → search_bulgarian_history; "What is the AUM history of A-1042?" → get_aum_history, not the history
  search); Scope: the history of Bulgaria is in scope, answered only from the tool; if the documentation does not
  cover it, say so; the rest of general knowledge stays out. `SystemPrompt.DefaultVersion = "system.v6"`;
  `Agent:SystemPrompt=system.v5` rolls back. `SystemPromptTests` move up one version.
- `OutOfScope.ReplyEnglish/ReplyBulgarian` add "and the history of Bulgaria" / "и с историята на България".

### D6. No new guard or answer-check context
- The content guard screens a tool result's items with the billing content context ("`untrusted_text` was returned …
  by a tool …: a document excerpt, a billing record, or a reviewer's verdict") — true for a history excerpt. Its five
  questions are about injection, not subject matter, so an article on a war or an uprising trips none of them. The
  codebase got its own context because repository text addresses AIs by design; nothing like that applies here. Kept
  as billing; the trace's `context` field reads `billing`, which is already the value for portfolio.
- The answer check's context names "fee billing and investment portfolios"; its two questions (relevant, grounded) are
  about `answer` against `user_question` and `sources`, so a history answer is judged correctly even though the
  framing sentence does not list the domain. Changing the sentence would reopen the §79 calibration for every suite.
  Left as is; if the generation eval later shows history answers graded `uncertain` more often, that is a change of
  its own.

### D7. Infrastructure, by the portfolio pattern
- compose: service `mcp-bulgarian-history` (build `src/Maf.Lab.BulgarianHistory/Dockerfile`, 2 replicas, `*app-env`,
  `BulgarianHistory__Collection/MetaCollection`, `depends_on qdrant healthy, ollama-warm completed`); api
  `Agent__Servers__2__Domain: bulgarian-history`, `Agent__Servers__2__Endpoint: http://lb/bulgarian-history/mcp`
  and `depends_on mcp-bulgarian-history healthy`; lb `depends_on`; `docker-compose.ci.yml` gets the `*ci-model-env`
  block for it. nginx: `upstream mcp_bulgarian_history_pool` and `location = /bulgarian-history/mcp` with the portfolio
  block's settings. The route table in README/copilot-instructions is generated from nginx.conf by `make docs`.
- Makefile: `BULGARIAN_HISTORY_REPLICAS ?= 2` in `up`'s `--scale` and the reload echo; `BULGARIAN_HISTORY_ENV :=
  Indexing__CorpusRoot=$(ROOT)/data-bulgarian-history Qdrant__Collection=maf_bulgarian_history_chunks
  Qdrant__MetaCollection=maf_bulgarian_history_meta`; a line each in `index` and `reindex`; target
  `index-bulgarian-history` with a `##` help line; `EVAL_HOST`/`eval` add
  `Evals__BulgarianHistoryMcpEndpoint=$(BASE_URL)/bulgarian-history/mcp`. `scripts/index_if_empty.sh` adds one
  `index_domain` line. `scripts/dev.sh` runs the server on :5093 and names it in the echo; `.vscode/launch.json` gains
  a configuration and the compound includes it; `compose/mcp-inspector/start.mjs` adds `'maf-lab bulgarian history':
  '/bulgarian-history/mcp'`; `appsettings.json` adds `{ "Domain": "bulgarian-history", "Endpoint":
  "http://localhost:5093/mcp" }`.
- Topology: `TopologyOptions.BulgarianHistoryService = "mcp-bulgarian-history"`; `TopologyProbe` resolves it, probes it
  with `DomainServerAsync("mcp-bulgarian-history", Domains.BulgarianHistory, …)`, adds the node id to `NodeIds` and
  edges api→it (`/bulgarian-history/mcp via lb`), it→qdrant (gRPC), it→ollama-embeddings (embed), it→otel-collector
  (OTLP). `docs/topology.drawio` gets the box (no overlap) and the four edges; `TopologyTests` cover the node and
  edges with the stub resolver.
- Eval host: `EvalOptions.BulgarianHistoryMcpEndpoint`; `EvalAgentHost` starts `Maf.Lab.BulgarianHistory.Program.BuildApp`
  in-process when empty and adds `new McpServerOptions { Domain = Domains.BulgarianHistory, Endpoint = … }` to
  `Servers`. `Maf.Lab.Eval.csproj` references the new project.

### D8. Evals and datasets
- `DatasetLoader.Tools` += `BulgarianHistoryTools.Search`; `SelectionCategories` += `"bulgarian-history"`;
  `DomainExpectations` += `"bulgarian-history"` and the `+`-joined validator accepts it. `DomainSuite` failure text
  prints the fourth probability; `Label` is unchanged (it orders by `Domains.All`). `Metrics.Domain` names its labels by hand: it gains
  `bulgarianHistoryRecall` (how often a history question puts the domain in scope at all, like `codebaseRecall`) and
  `notConfused` counts `bulgarian-history` among the single-domain labels.
- `evals/domain.jsonl`: ~12 positive rows (EN 4, BG 5, bg-latn 3; design/holdout split) — the baptism, Simeon I, the
  April uprising, the Russo-Turkish war, the unification, Ottoman rule, the Revival, the first and second states — and
  8 negative rows, two per exclusion: AUM/market value over quarters → `portfolio`; past runs → `billing`; earlier
  conversations with the assistant → `none`; the code's commits → `codebase`. `evals/selection.jsonl`: 5 rows
  (EN/BG/bg-latn) expecting `search_bulgarian_history`, category `bulgarian-history`, firm-a and firm-b alternating.
- Run `intent`, `domain` and `selection` against the stack (needs `OLLAMA_API_KEY` and `JEV_MAF_LAB`); compare with the
  baselines; record in DECISIONS §81 the floor-sweep table for the history cases. The baseline moves only by
  `make eval-accept`, the owner's call. If the keys are absent in apply, the task records that the suites were not run
  and why; it does not fake a result.

### D9. Tests that enumerate projects
- `QueryPathEnumerationTests.ProductAssemblies` gains `Maf.Lab.CodeSearch`, `Maf.Lab.Portfolio` and
  `Maf.Lab.BulgarianHistory` (the first two are missing today — a gap the report found). `GraphStoreTests.ProductAssemblies`
  gains `Maf.Lab.BulgarianHistory`. Both read dlls from the test output, so `Maf.Lab.Tests.csproj` references the new
  project; `Maf.Lab.IntegrationTests.csproj` too, for the acceptance test's in-process server.
- `TestHostContentRootTests.Hosts` is about A2A audiences and does not list MCP servers; unchanged.

### D10. Web
- `toolLabels.ts`: `search_bulgarian_history` → "Searching Bulgarian history…" / "Searched Bulgarian history".
- `domainData.ts` `domainColor`: `'bulgarian-history'` → a fourth token (`var(--kind-source)` or whichever existing
  kind token is free; no new CSS variable).
- `admin/toolNames.ts` += `'search_bulgarian_history'` (kept in step with `DatasetLoader.Tools`).
- `ChatPage` placeholder adds "…, Bulgarian history". Vitest tests for the label and the colour.

## Risks / Trade-offs

- [The word "history" drags AUM, chat, run and commit questions into the domain] → descriptive exclusions in the
  Noul (D3), the same cure as §54; negative eval rows for each (D8); the data router never routes to the domain.
- [A history question with a billing word crosses into billing and forces two searches] → the relevance gate silences
  the search that has nothing (§36); the answer names where each part came from, as every crossing does.
- [The corpus is ten articles; many history questions find nothing] → the forced search returns "no matching
  documentation" and v6 tells the model to say so instead of answering from weights; the answer check's grounding
  question still applies.
- [Bulgarian Cyrillic text and `embeddinggemma`/BM25] → the model is multilingual by choice (project.md) and the BM25
  tokenizer already handles Cyrillic for the billing corpus's Bulgarian rows; the acceptance test queries in Bulgarian.
- [One more Noul moves the other answers] → Jev answers each question independently over the same state (rule §1);
  the `intent` and `domain` reruns confirm.
- [Two more assemblies in `QueryPathEnumerationTests` surface an existing rogue call in Portfolio or CodeSearch] → that
  would be a real finding; the test is right and the call is fixed or recorded, never the test loosened.

## Migration Plan

- `make` (or `make up` then `make index-bulgarian-history`): the new image builds, the collection is created and
  indexed when empty, the balancer reloads with the new pool. Nothing migrates: a new collection, a new service.
- Rollback: `Agent:SystemPrompt=system.v5`; remove `Agent__Servers__2__*` from the api. The Noul stays harmless — a
  domain with no server offers no tools and forces nothing it cannot call (`ForcedSearches` filters by offered tools).
  The collection can be left or dropped by hand.

## Open Questions

- Which existing colour token the Domains view uses for the fourth domain (cosmetic; decided in apply).

# Design

## Context

One MCP server serves the billing domain. The api opens one MCP client per turn, and Jev classifies the intent and
answers one domain question (`in_domain`). The turn then forces `search_documents` or routes a billing read tool. The
trace records tool calls but not which server or domain served them, because there was only one.

## Goals / Non-Goals

**Goals:**
- A second domain served by its own MCP server, corpus, collection and RAG tool.
- Jev, and only Jev, decides which domains a question touches.
- The trace and the monitor show the crossing: predicted (Jev) and actual (the calls made).

**Non-Goals:**
- Portfolio writes (trades, rebalances). The portfolio server is read-only.
- Jev routing of portfolio read tools. The model chooses them; only billing keeps the router it already has.
- A general N-domain registry in the UI. The code is written for a list of domains, and the lab has two.

## Decisions

### D1. A separate server, collection and vocabulary per domain
- `mcp-portfolio` is its own ASP.NET host (`Maf.Lab.Portfolio`). It reuses the retrieval core by project reference:
  `AddMafRetrievalCore`, dev JWT, the Jev judge.
- Its options point at `maf_portfolio_chunks` and `maf_portfolio_meta`.
- Every query still goes through `TenantScopedSearch.QueryAsync`, the one method that applies the tenant filter.
  Nothing new builds a Qdrant query: the collection is configuration, not a parameter.
- A separate BM25 vocabulary is deliberate: IDF from a billing corpus would mis-weight portfolio terms.
- *Alternative:* a `domain` payload field in one collection. Rejected:
  - Every existing chunk would need a re-index.
  - The one query method would gain a second filter dimension.
  - A domain would stop owning its data.

### D2. The api merges tool sets and remembers ownership
- `Agent:McpEndpoint` stays the billing server. `Agent:Servers` may add more (`{ Domain, Endpoint }`); compose
  configures `portfolio → http://lb/portfolio/mcp`.
- `McpToolSource` connects to all servers in parallel, with the same user bearer on each.
- `ToolSet` carries `DomainOf(tool)` and `ServerOf(tool)`. When a name repeats, the first server keeps it and the
  duplicate is logged and dropped.
- A server that cannot be reached leaves its tools out of the turn, rather than failing the turn. The trace's prompt
  event lists the domains that are offered.
- A confirmation is sent to the client that owns the tool.

### D3. Jev answers one yes/no per domain, in the same request
- `in_domain` (billing, unchanged id) and `in_portfolio`, both `JevNoulQuestion` with the domain described beside the
  question.
- Independent Nouls rather than one Choice among billing / portfolio / both / neither: a crossing question should be
  able to score high on both, and a Choice would split its probability between them.
- The gate that lets a forcing intent act uses the highest domain probability against `MinInDomain` (0.2, unchanged).
- **In scope:** a domain at or above `Jev:MinDomainScope` (default 0.5, calibrated by the domain suite).
- When the gate passes and no domain reaches scope, the most probable domain is in scope alone. This keeps today's
  billing behaviour for a question at, say, 0.37.
- **Crossing:** two or more domains in scope.

### D4. Forced searches per domain
- `RequiredToolModeChatClient` takes the list of search tools to force: one per domain in scope that is offered.
- On the first request it issues all of them in one assistant message, as parallel calls. `FunctionInvokingChatClient`
  runs them and clears the required mode for the next iteration.
- With emulation off, only the first is required. The provider's `tool_choice` names one function, and the model may
  call the rest.

### D5. The crossing is derived from calls, not claimed by the model
- Each tool call is stamped with the domain from the tool set.
- `boundary` is emitted when a call's domain differs from the domain of the previous call in the turn. A parallel
  forced pair therefore shows `billing → portfolio` once.
- `turn.end` carries `domainPath` (collapsed consecutive duplicates), `domainsTouched`, and `predicted` (Jev's in-scope
  set). The monitor shows where prediction and reality differ.

### D6. Portfolio seed data mirrors billing accounts
- `compose/seed/portfolio-households.json` uses the account ids of `billing-accounts.json`, so a question about
  A-1042 means the same account in both domains.
- A record's free-text note never leaves the store, as in billing.

## Risks / Trade-offs

- **The prompt text changes.** Selection metrics may move. Mitigation: rerun `make eval SUITE=selection,intent` and
  record the result in DECISIONS.md.
- **Two parallel searches cost two Jev relevance requests.** This is accepted: the crossing is the point, and each
  appears in the trace.
- **The scope floor is a new threshold.** Mitigation: the domain suite, with its labelled crossing questions,
  calibrates it.

# Proposal

## Why

maf-lab has one domain: fee billing. Every tool lives on one MCP server (`mcp-retrieval`) and every document sits in one
collection. So the lab cannot show what a multi-domain assistant is really about: a question that starts in one domain
and can only be answered from another. "Why did the Ridgeline Family Trust's fee go up this quarter?" is a billing
question, but the answer is in the portfolio. Its quarter-end AUM crossed a fee band. A lab that exists to observe its
own internals should make that crossing visible: which domains the classifier saw, which server each call went to, and
where the turn stepped from one domain into the other.

## What Changes

- A second MCP server, `mcp-portfolio`, owns a Portfolio domain:
  - Its own documentation corpus (`data-portfolio/`: model portfolios, rebalancing, drift, AUM valuation, cash, held-away
    assets, performance reporting) indexed into its own Qdrant collection and BM25 vocabulary.
  - Its own RAG tool, `search_portfolio_documents`, running the same tenant-scoped hybrid search, relevance gate and
    reranker as `search_documents`.
  - Two read tools over seeded household portfolios: `get_household_portfolio` (holdings, allocation against the model,
    drift, AUM) and `get_aum_history` (quarter-end AUM, the number the billing desk bills on).
  - Two replicas behind the balancer at `/portfolio/mcp`, authenticated with the same user token. Tenant from the
    principal only.
- The api consumes both servers:
  - One tool set per turn, merged from every configured server.
  - Each tool is tagged with the domain and server that own it.
- Jev decides the domains, in the same request that classifies the intent:
  - A second yes/no question asks whether the question is about the portfolio domain, beside the existing billing one.
  - The domain gate passes when either domain reaches its floor.
  - Each domain at or above a scope floor is **in scope**, and a question with two domains in scope **crosses** the
    boundary.
  - A forcing intent searches every domain in scope: both `search_documents` and `search_portfolio_documents` when
    the question crosses.
  - Data routing to the billing read tools applies only when billing is in scope.
- The trace shows the crossing:
  - A `domain` event after `intent`: each domain's probability, the scope floor, the domains in scope, and whether the
    question crosses.
  - `domain` and `server` on every `tool.forced`, `tool.call` and `tool.result`.
  - A `boundary` event each time a tool call enters a different domain from the call before it.
  - The domain path of the turn (e.g. `billing → portfolio`) in `turn.end`.
- The monitor gains a **Domains** view: Jev's domain verdict, the path the turn took across servers, and each hop. It
  also gains a header chip naming the path.
- The system prompt names the portfolio tools and says when a question needs both domains.
- A `domain` eval suite (`make eval SUITE=domain`) measures Jev's domain verdict (billing, portfolio, both, neither)
  on labelled questions, in English and Bulgarian.

## Capabilities

### New Capabilities
- `portfolio-mcp`: the Portfolio MCP server, its corpus, its RAG tool and its read tools.

### Modified Capabilities
- `intent-classification`: the domain question per domain, the scope floor, crossing, and search forced per domain.
- `chat-agent`: tools merged from several MCP servers, each tagged with its domain; searches forced per domain.
- `turn-tracing`: `domain` and `boundary` events, domain and server on tool events, the domain path on `turn.end`.
- `web-ui`: the Domains view and the path chip.
- `load-balancing`: `/portfolio/mcp` routes to the portfolio pool.
- `document-indexing`: a second corpus root indexed into its own collection.

## Impact

- New project `src/Maf.Lab.Portfolio`, Dockerfile, compose service `mcp-portfolio` (x2), nginx upstream.
- `Maf.Lab.Api`: `McpToolSource`, `ToolSet`, `JevIntentClassifier`, `ChatTurnRunner`, `RequiredToolModeChatClient`,
  the system prompt (`system.v1` stays the file; its text changes, so selection evals must be rerun).
- `Maf.Lab.Domain`: portfolio DTOs, `TraceKinds.Domain`/`Boundary`.
- `web/`: monitor tab, kind colours, tool labels.
- Makefile: `make index` indexes both corpora; `index-if-empty` checks both collections.
- DECISIONS.md: a section for the domain boundary. No package version moves.

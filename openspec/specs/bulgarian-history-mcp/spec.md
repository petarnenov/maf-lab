# bulgarian-history-mcp Specification

## Purpose
The fourth domain's own MCP server: the history of Bulgaria, a corpus that is shared by nature, indexed into its own
collection and searched by one tool through the one tenant-scoped query method, so that every firm and every advisor
reads the whole of it and gets the same answer.

## Requirements

### Requirement: Bulgarian history MCP server
The system SHALL run a fourth MCP server, `mcp-bulgarian-history`:
- It SHALL speak the same protocol (Streamable HTTP, stateless) and require the same user bearer token as
  `mcp-retrieval` and `mcp-portfolio`.
- It SHALL derive the principal from the token only.
- It SHALL expose exactly one tool, `search_bulgarian_history`, marked read-only, idempotent and not destructive.
- The tool's input SHALL carry no tenant, firm, user or advisor parameter.
- It SHALL run as at least two replicas behind the load balancer.

#### Scenario: Tools listed
- **WHEN** an authenticated client lists the server's tools
- **THEN** it receives exactly `search_bulgarian_history`, marked read-only, with an input schema that names no tenant, firm, user or advisor

#### Scenario: Unauthenticated call
- **WHEN** a client calls the server without a valid bearer token
- **THEN** the request is rejected with 401

### Requirement: Bulgarian history RAG tool
`search_bulgarian_history`:
- SHALL search only the Bulgarian history corpus, indexed into its own collection and BM25 vocabulary, never the billing,
  portfolio or code collection.
- SHALL go through the one tenant-scoped query method with the caller's principal, like every other search.
- SHALL return the same result shape as `search_documents`: snippets with document, section and source path, never a
  synthesized answer.
- SHALL use the same hybrid search, relevance gate and reranker, and SHALL return retrieval diagnostics in the result
  `_meta` when the caller asks for them.
- SHALL say in its description what it is for (the history of Bulgaria) and what it is not for (billing, portfolios and
  the lab's code, each with the tool that is), in the same "Use when / Do not use for" form as the other searches.

#### Scenario: A history question answered from the history corpus
- **WHEN** a user searches "кога е покръстването на българите"
- **THEN** every snippet comes from a document of the Bulgarian history corpus, and none from another collection

#### Scenario: Empty query
- **WHEN** the tool is called with an empty query
- **THEN** it returns a tool error asking for a natural-language phrase, and nothing is searched

### Requirement: A shared-only corpus every principal reads whole
The Bulgarian history corpus SHALL be laid out with the single tenant folder `shared`, so that every chunk it produces
carries the shared tenant and the corpus layout holds no firm. The tool SHALL NOT bypass, widen or add to the tenant
filter: every principal reads `[own firm, shared]` as today, and because the corpus is entirely shared, every principal
reads all of it. Advisor ids SHALL play no part in the result, as they play none in any retrieval today.

#### Scenario: The corpus is shared and nothing else
- **WHEN** the indexer loads `data-bulgarian-history`
- **THEN** every document's tenant is `shared`, the layout's tenants are exactly `{shared}`, and no document is rejected

#### Scenario: Every firm and every advisor gets the same result
- **WHEN** an advisor of firm-a, an advisor of firm-b and a user of firm-c with different advisor ids search the same
  phrase in the Bulgarian history collection
- **THEN** the three results are identical: the same documents, sections and snippets in the same order

#### Scenario: No new query path
- **WHEN** the product assemblies are scanned for Qdrant data-plane calls
- **THEN** the new server's assembly adds none outside the one tenant-scoped query method

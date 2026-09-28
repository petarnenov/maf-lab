# Tasks

## 1. Portfolio domain: data, corpus, server

- [x] 1.1 Add portfolio DTOs to `Maf.Lab.Domain/Portfolio` (household portfolio, holding, AUM history).
- [x] 1.2 Seed `compose/seed/portfolio-households.json` with the billing account ids, holdings, the model portfolio and
  quarter-end AUM.
- [x] 1.3 Write the `data-portfolio/` corpus: shared docs and procedures, plus firm-a, firm-b and firm-c docs.
- [x] 1.4 Create the `Maf.Lab.Portfolio` project:
  - `Program`, Dockerfile, `portfolio.json` pointing at `maf_portfolio_chunks` / `maf_portfolio_meta`;
  - the store;
  - `search_portfolio_documents`, `get_household_portfolio` and `get_aum_history`.
- [x] 1.5 Unit tests:
  - the store's tenant scoping;
  - no note in the DTO;
  - the tool list and annotations.

## 2. Indexing and topology

- [x] 2.1 `make index` / `reindex` / `index-if-empty` cover both corpora; add a `make index-portfolio` target.
- [x] 2.2 Compose: an `mcp-portfolio` service (x2); nginx `/portfolio/mcp` → `mcp_portfolio_pool`; the api's
  `Agent__Servers__0__*`.
- [x] 2.3 `make dev`: run the portfolio server on 5091.

## 3. api: tools from several servers

- [x] 3.1 `AgentOptions.Servers`, and `ToolSet` with its domain and server maps.
- [x] 3.2 `McpToolSource`:
  - connect to every server;
  - merge the tools, dropping duplicates;
  - tolerate a server that is down;
  - send a confirmation to the owning client.
- [x] 3.3 Treat `search_portfolio_documents` as a search: sources, the searched flag, guard excerpts, the review queue.

## 4. Jev domains

- [x] 4.1 Add the `in_portfolio` Noul, `Domains` on `IntentDecision`, `MinDomainScope`, in-scope and crossing; the
  gate uses the maximum.
- [x] 4.2 Route billing tools only when billing is in scope.
- [x] 4.3 Unit tests with `FakeJev`:
  - portfolio only;
  - crossing;
  - off-domain;
  - no route when billing is out of scope.

## 5. Forcing and tracing

- [x] 5.1 `RequiredToolModeChatClient` issues every forced search together.
- [x] 5.2 `ChatTurnRunner`:
  - the `domain` event;
  - domain and server on tool events;
  - `boundary` events;
  - the domain path in `turn.end`.
- [x] 5.3 System prompt: the portfolio tools and crossing examples.
- [x] 5.4 Tests: a crossing turn traces a boundary and the path; a single-domain turn traces none.

## 6. Web

- [x] 6.1 Types, kind colours and tool labels for the portfolio tools.
- [x] 6.2 The Domains tab and the header path chip, with tests.

## 7. Evals and docs

- [x] 7.1 `evals/domain.jsonl` and a `DomainSuite` (`SUITE=domain`); add portfolio and crossing rows to
  `selection.jsonl`.
- [x] 7.2 Calibrate `MinDomainScope` against the domain suite.
- [x] 7.3 DECISIONS.md section; README, project.md layout, `docs/trace-events.md`.

## 8. Verify

- [x] 8.1 `make test`, `make lint`.
- [x] 8.2 `make` on the live stack: portfolio indexed, both servers healthy, a crossing question traced end to end.

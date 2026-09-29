# Tasks

## 1. Domain and store

- [x] 1.1 In `Maf.Lab.Domain/Portfolio/PortfolioDtos.cs`, add `PortfolioTools.ListAccounts = "list_my_accounts"`,
  `AccountSummary` and `AccountList`.
- [x] 1.2 In `PortfolioStore`, extract the firm predicate into `Owns(principal, record)` and use it in `Find`. Add
  `List(principal)`, ordered by account id.
- [x] 1.3 Unit tests in `PortfolioDomainTests`:
  - firm-a gets A-1042, A-1043 and A-1044 in order;
  - firm-b gets B-200 and B-201 only;
  - no canary text in the serialized list;
  - an unknown firm gets an empty list.

## 2. Tool

- [x] 2.1 Add `HouseholdTools.ListMyAccounts` with read-only annotations, the output schema and a house-style
  description. It uses `Read`/`WithInstance` and logs only the count.
- [x] 2.2 Update the "Do not use for" text of `get_household_portfolio`, `get_aum_history` and
  `search_portfolio_documents` (`PortfolioSearchTool.cs`).
- [x] 2.3 Server tests:
  - the tool-list assertion (`PortfolioDomainTests` ~line 96) now includes the new tool, read-only, with no inputs at
    all;
  - an MCP call as firm-a returns the structured list;
  - an MCP call as firm-b returns no firm-a id.

## 3. Routing and trace

- [x] 3.1 `DataToolRouter`: add the tool to `ReadTools`, `ToolDomain` and `ToolDescriptions`. In `Route`, add the
  no-account branch (D4).
- [x] 3.2 `ChatTurnRunner.Summarise`: add a case for `list_my_accounts` (for example, "N accounts").
- [x] 3.3 Tests:
  - the Jev question ids in `DataToolRoutingTests` (~line 181);
  - a `FakeJev` score for the new tool;
  - `FakeTools` includes the tool;
  - routing tests for "Which accounts do I have access to?", which routes with no arguments, and for the list winning
    with A-1042 named, which gives no route and the stated reason.

## 4. Prompt

- [x] 4.1 Add the tool and one example to the Portfolio section of `Prompts/system.v2.md` and `system.v1.md`.

## 5. Web

- [x] 5.1 In `web/src/chat/toolLabels.ts`, add "Listing your accounts…" / "Listed your accounts", with a test in
  `toolLabels.test.ts`.
- [x] 5.2 Update `web/src/admin/toolNames.ts` to list every current tool, the new one included.

## 6. Evals and docs

- [x] 6.1 Add the tool to `DatasetLoader.Tools` (`src/Maf.Lab.Eval/Datasets/Datasets.cs`).
- [x] 6.2 Add `selection.jsonl` rows, in English and Bulgarian, all expecting `list_my_accounts`. For example:
  "Which accounts do I have access to?", "Покажи ми сметките, до които имам достъп", and "What households do I manage?"
  Add one row that names an account, to check that it still goes to the per-account tool.
- [x] 6.3 Update DECISIONS.md: add the tool to the portfolio read-tools section, the firm-scope decision, and the eval
  numbers from before and after.

## 7. Verify

- [x] 7.1 `make test`, `make lint`.
- [x] 7.2 `make eval SUITE=selection` and `make eval SUITE=intent`, with no regression against the baseline.
- [x] 7.3 On the live stack (`make`), log in as `adam` and ask "Кои акаунти мога да виждам?". The turn should be
  routed to `list_my_accounts` and should answer with the firm-a accounts only. As `bianca`, it should show only the
  firm-b accounts.

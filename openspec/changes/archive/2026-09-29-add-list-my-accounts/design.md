# Design

## Context

The portfolio server has two per-account read tools. Each one looks up one record through `PortfolioStore.Find`, and
the lookup matches `principal.FirmId`. The billing server's `BillingAccountStore` applies the same firm rule, and both
seeds hold the same seven account ids.

The `Principal` also carries `AllowedAdvisorIds`, but no code filters on it, and no seed record names an advisor. So
the access a user has today is exactly "the accounts of my firm".

## Goals / Non-Goals

**Goals:**
- A user, and the model on their behalf, can see which accounts they can ask about.
- The list and the per-account tools agree. An account appears in the list if and only if the per-account tools will
  answer for it.

**Non-Goals:**
- Per-advisor or per-household entitlements (see the proposal, "Out of scope").
- Paging or filtering. A firm has a handful of accounts in the seed; a real book would need paging, and the DTO leaves
  room for it (see D2).
- Balances or AUM in the list. Those stay with the per-account tools, so that the list stays cheap and safe to call
  first.

## Decisions

### D1. The tool lives on the portfolio server
- The portfolio records hold the household id and the model portfolio, which the billing records lack.
- Both seeds hold the same account ids, so the list is equally valid for billing questions. Adding it to the retrieval
  server as well would mean two tools with one answer, and a selection problem the evals would have to untangle.
- The account belongs to the `portfolio` domain for routing and tracing. A billing-only question that needs the list
  is a crossing, and the trace already shows crossings.

### D2. Store method and DTO
- `PortfolioStore.List(Principal principal)` filters `_records` by `r.FirmId == principal.FirmId.Value`, the same
  predicate as `Find`, and orders by account id with ordinal comparison.
  - The predicate is extracted into one private `Owns(principal, record)` used by both `Find` and `List`. This keeps
    one firm rule in the store. The Qdrant rule (one query method) is untouched, because this store does not query
    Qdrant.
- DTOs in `Maf.Lab.Domain/Portfolio/PortfolioDtos.cs`:
  ```csharp
  public sealed record AccountSummary(string AccountId, string Name, string HouseholdId, string ModelPortfolio, string Currency);
  public sealed record AccountList(int Count, IReadOnlyList<AccountSummary> Accounts);
  ```
  The DTOs are built field by field from the record, never serialized from it, so neither `Note` nor `Holdings` can
  leak.
- `PortfolioTools.ListAccounts = "list_my_accounts"`.

### D3. Tool shape
- The tool is a `HouseholdTools.ListMyAccounts(RequestContext<CallToolRequestParams>? context = null)` method, with the
  same attributes as its siblings: `ReadOnly`, `Idempotent`, not `Destructive`, not `OpenWorld`,
  `UseStructuredContent`, and `OutputSchemaType = typeof(AccountList)`.
- It reuses `Read`/`WithInstance`, so exceptions map through `ToolErrors` and the instance `_meta` still appears.
  An empty list is a normal result, not a not-found error.
- The description follows the house style:
  - what the tool returns;
  - **Use when:** "which accounts do I have", or when the user has not named an account for a per-account question;
  - **Do not use for:** holdings or AUM of an account (`get_household_portfolio`, `get_aum_history`) and documentation
    (`search_portfolio_documents`).
- Logging carries the tool name and the row count, never names.
- *Name:* `list_my_accounts` rather than `list_accounts`. The "my" tells the model that the scope is the caller's own,
  so it will not try to pass a firm or user.

### D4. Routing
- The tool is added to `ReadTools`, to `ToolDomain` (portfolio) and to `ToolDescriptions`. Jev gets one more Noul,
  `tool_list_my_accounts`.
- In `Route`, the portfolio branch splits:
  - `list_my_accounts` routes with empty arguments when `AccountIds(question)` is empty. Otherwise it gives no route,
    with the reason "list_my_accounts takes no account id, the question names N". The question is about a specific
    account, and the model should choose.
  - The per-account tools keep the rule of exactly one account id.
- *Alternative:* never route the list and leave it to the model. Rejected, because "which accounts can I see" is the
  clearest possible data question, and routing it is cheap and deterministic.

### D5. Prompt and descriptions
- In `system.v2.md` (the active prompt) and `system.v1.md`, add the tool to the Portfolio list, and add one example: "the
  user asks about 'my accounts' or gives no account id, so call `list_my_accounts` first, then the per-account tool."
- The "Do not use for" text in `get_household_portfolio`, `get_aum_history` and `search_portfolio_documents` points
  "which accounts exist" to `list_my_accounts`.

## Risks / Trade-offs

- **Tool-selection drift.** A fifth portfolio-side tool and new prompt text can move selection. Mitigation: new rows
  in `evals/selection.jsonl`, then `make eval SUITE=selection` and `SUITE=intent` before and after. Record the numbers
  in DECISIONS.md.
- **An extra Jev question per turn.** It adds a little latency and cost to each classification. This is accepted,
  because it is the same as each existing tool.
- **Firm scope is coarse.** An advisor sees the whole firm's accounts. That matches what the other tools already
  allow, and the proposal records it as out of scope.

# Proposal

## Why

Every portfolio and billing read tool takes one account id, and nothing tells the user or the model which ids exist.
"Which accounts can I see?" today falls to a documentation search that cannot answer it. The model then either guesses
ids or asks the user for one they may not know. A tool that lists the caller's accounts closes the loop: the user
discovers the accounts, and the model has real ids to pass to `get_household_portfolio` and `get_aum_history`.

## What Changes

- A new read-only tool on the portfolio server, `list_my_accounts`, returns the accounts the logged-in principal can
  access.
  - **Access is the firm scope:** every account of the principal's firm. This is the same rule
    `get_household_portfolio` and `get_aum_history` apply, so the list never shows an account that another tool would
    refuse, and never hides one that a tool would return.
  - **No arguments.** The tenant comes from the token, as for every tool.
  - Each account row carries the account id, name, household id, model portfolio and currency, ordered by account id.
    The result also carries the count.
  - The record's internal note and its holdings are never part of the result.
- Jev routing learns the tool. A data question in the portfolio domain that names **no** account id can be routed to
  `list_my_accounts`, with no arguments.
- The system prompt names the tool and when to use it: before a per-account tool when the user has not named an
  account, and when the user asks which accounts they have.
- The other portfolio tools' descriptions point to `list_my_accounts` for "which accounts".
- The web shows a label for the call ("Listing your accounts…").
- Selection eval rows for the tool, in English and Bulgarian.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `portfolio-mcp`: the server exposes four tools; there is a new account-list requirement.
- `intent-classification`: portfolio routing covers the account list, which is routed only when no account id is named.

## Impact

- `Maf.Lab.Domain/Portfolio/PortfolioDtos.cs`: the tool name constant and the `AccountSummary` / `AccountList` DTOs.
- `Maf.Lab.Portfolio`: `PortfolioStore.List(principal)` and the tool method in `HouseholdTools`.
- `Maf.Lab.Api`: `DataToolRouter` (read tools, domain, Jev description, the no-account rule), the `ChatTurnRunner`
  trace summary, and the system prompts `system.v2.md` / `system.v1.md`. The prompt text changes, so the selection
  and intent evals must be rerun.
- `Maf.Lab.Eval`: `DatasetLoader.Tools`, plus new rows in `evals/selection.jsonl`.
- `web/`: `toolLabels.ts`, and `admin/toolNames.ts`, which is brought up to date with every tool.
- Tests: the portfolio tool list, firm scoping of the list, routing ids, `FakeJev`, and `FakeTools`.
- Every Jev classification request gains one routing Noul question. No package version moves.

## Out of scope

- Per-advisor access. The principal carries `AllowedAdvisorIds`, but no seed record has an advisor, and no tool
  enforces one. Narrowing the list without narrowing every per-account tool would make the list lie about access. That
  belongs in its own change, which would cover all tools together.

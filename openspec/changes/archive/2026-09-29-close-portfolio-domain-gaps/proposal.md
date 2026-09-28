# Proposal

## Why

add-portfolio-domain made the domain boundary visible and left ten gaps behind it:
- **Unpushed work and a stale web container.**
- **A balancer that loses its configuration on every git checkout.**
- **Known misjudged crossings and a thin crossing dataset.**
- **Eval coverage:** no retrieval eval for the portfolio corpus.
- **No Jev routing for portfolio data questions.**
- **Review queue:** it resolves portfolio sources against billing's collection. Its labels would land portfolio chunks
  in billing's retrieval eval.
- **Jev statistics:** they cannot tell the domains apart.
- **Forcing on providers that honour `tool_choice`:** a crossing still forces only one search there.

## What Changes

- **Balancer:** it mounts `compose/lb/` as a directory and starts with `-c`, so a replaced `nginx.conf` is read on reload.
- **Forcing:** a turn that forces more than one call (a crossing, or a run beside its search) is emulated whatever
  `Agent:EmulateRequiredToolMode` says.
- **Routing:** Jev routes data questions to `get_household_portfolio` / `get_aum_history` (exactly one account id from
  the question). Routing is always among the tools of the domains in scope, never billing's for a portfolio question.
- **Retrieval eval:** rows carry a `domain`. Portfolio rows (EN, BG, BG-Latin, off-domain) are scored against
  `maf_portfolio_chunks` as the `portfolio-hybrid` variant, with its own thresholds (`retrieval-portfolio`).
- **Review queue:** it resolves each search's sources in its own domain's collection. A retrieval label records the
  domain its chunks came from and refuses chunks from both.
- **Jev statistics:** judged searches per domain, and a Domains section. It counts turns per verdict (billing,
  portfolio, both, none), turns that crossed, and turns whose calls matched the verdict.
- **Domain eval:** 16 more held-out questions (8 crossing). The domain descriptions Jev reads are sharpened against the
  measured errors; before and after are recorded in DECISIONS.

## Capabilities

### Modified Capabilities
- `intent-classification`: routing to portfolio read tools, routing within the domains in scope.
- `chat-agent`: several forced calls are always emulated.
- `eval-harness`: retrieval per domain, domain labels on review-queue retrieval rows.
- `jev-statistics`: per-domain relevance and the Domains section.
- `load-balancing`: the configuration directory is mounted, not the file.

## Impact

- **api:** `DataToolRouter`, `JevIntentClassifier`, `ChatTurnRunner`, `FeedbackEndpoints`, `JevStatistics`, `Program`.
- **Eval:** `RetrievalSuite`, `Program`, datasets.
- **web:** the Jev page.
- **Infrastructure:** compose and Makefile.
- No package moves.

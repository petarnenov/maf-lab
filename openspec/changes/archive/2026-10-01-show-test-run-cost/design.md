# Design

## Context

See proposal.md for why. What exists today:

- The table is the "Recent runs" table of `web/src/admin/TestAgentSection.tsx` on `/admin/a2a` (columns File, State,
  Attempt, Coverage, Reason, Model, Duration, When), fed by `GET /api/admin/a2a/test-agent`
  (`Coverage.TestAgentOverview.BuildAsync`), whose `recent` items are `TestAgentRun` DTOs built by
  `TestAgentOverview.Recent` from `TestGenRunRow`s.
- Spend is already recorded per run. When the api starts a run it sends the agent the chosen model's
  `ModelPrice(InputPerMTok, OutputPerMTok)` from the configured allowlist (`TestGenRuns.StartAsync`). The agent's
  `RunUsage` adds up every model call's input and output tokens (`BudgetedChatClient`) and prices them with that
  price (`AttemptEstimate.Cost`, rounded to 4 decimals); `RunUsage.CostUsd` is what the cost cap is compared with. The
  agent reports it in every progress update and in the final report (`TestGenUsage.EstimatedCostUsd`), and
  `TestGenRuns.ApplyAsync` stores it on the row as `Tokens` and `CostUsd`. A restarted task restores its usage from its
  checkpoint, so the count survives a restart.
- `CostUsd` and `Tokens` are non-null columns that have existed since the first run, so every stored run has them; a
  run that never reached a model call has 0. The cost caps are on the row (`BudgetTokens`, `BudgetCostUsd`).
- The price is not stored on the row; whether a model's price is an estimate is `AgentModelOption.PriceIsEstimate` in
  the allowlist (all models are estimates in today's configuration).
- The Coverage page already shows a run's tokens and cost (`RunStatus`, `RunActivity`) from `RunSummary`.

## Goals / Non-Goals

**Goals:**
- The Cost column shows the same amount the budget counted, so a run stopped by its cost cap reads at or just above
  that cap.
- No new measurement, column or contract: surface what is recorded.
- Say honestly when the price behind the amount is an estimate.

**Non-Goals:**
- No change to the Coverage page, which already shows cost.
- No recomputation from today's prices, no per-attempt cost, no runner compute cost (that is not money).
- No polling and no ticking: unlike the duration, the cost cannot be extrapolated; it updates on refresh.

## Decisions

1. **Cost = the run's recorded `CostUsd`, priced at the start-time price the budget used.** Alternatives: recompute
   from `Tokens` × the current catalog price — rejected, the column would drift from the budget enforcement whenever a
   price is edited, and a run stopped at its $0.50 cap could read $0.62; store a price snapshot on the row — rejected
   as unnecessary, the agent already stores the priced amount. Recorded in DECISIONS §73.

2. **DTO: `Tokens`, `CostUsd`, `CostIsEstimate`, `Budget` added to `TestAgentRun`** (additive JSON). `Budget` is the
   existing `RunBudget(MaxTokens, MaxCostUsd)` from the row, as `RunSummary` carries it. `CostIsEstimate` is looked
   up in the configured allowlist by the run's model tag (`PriceIsEstimate`); a model no longer on the allowlist reads
   as an estimate, since nothing says its price was a list price. The flag is today's configuration, not a snapshot —
   acceptable: it qualifies the price's source, which changes only when someone replaces an estimate with a list
   price. `CostUsd` stays non-null on the server because every row has it; the web still treats a missing value as
   unknown (`—`), which is what an api replica from before this change answers during a rolling update.

3. **Web: a `Cost` column after `Duration`, formatted by `cost(usd)` in `web/src/coverage/format.ts`.** `$0.042`
   (three decimals) under $1, `$1.27` (two decimals, grouped) from $1, `<$0.001` above zero but below what three
   decimals show, `$0.00` for zero, `—` for null/undefined/non-finite. An estimate above zero gets `≈`. A running run
   (the Duration column's `isRunning`) adds a muted "so far". The `title` reads e.g. "2,760,003 tokens · estimated
   price · of $0.50 budget". The existing `usd` helper (the picker's estimate) is left as it is.

## Risks / Trade-offs

- [The recorded cost of a run that failed mid-call may miss that call's usage] → It is what the budget counted too;
  the column and the cap agree, which is the point.
- [Every model is an estimate today, so every amount reads `≈`] → True to the configuration; it changes by itself
  when a list price is configured.
- [A model removed from the allowlist shows `≈` even if its price was a list price] → Rare, and erring towards
  "estimate" is the safe side.

## Migration Plan

Additive fields on an existing answer; no schema change. Rolling back removes the fields and the column.

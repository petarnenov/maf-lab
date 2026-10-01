# Proposal

## Why

The "Recent runs" table of the test-generation agent section on the Agent to agent page (`/admin/a2a`) says how long
each run took but not what it cost. An operator comparing models or limits has to open each run on the Coverage page
to see the money, one file at a time. Every run already records its tokens and its cost — the same number its cost cap
is checked against — so the table can say it without anything new being measured.

## What Changes

- Each recent run in `GET /api/admin/a2a/test-agent` gains `tokens`, `costUsd`, `costIsEstimate` and `budget`
  (`{ maxTokens, maxCostUsd }`, the caps chosen at start). `costUsd` is the run's recorded spend in USD: the agent's
  model calls priced at the model's rates as sent to the agent when the run started — the value the cost cap counts.
  It is not recomputed from today's prices. `costIsEstimate` is true when the run's model is priced at the lab's
  estimated rates (`priceIsEstimate` in the allowlist), or when the model is no longer on the allowlist.
- The Recent runs table gains a **Cost** column right after **Duration**: `$0.042` under a dollar, `$1.27` from a
  dollar, `<$0.001` for a non-zero amount under a tenth of a cent, `$0.00` when nothing was spent (no model call, or
  a model priced at zero), `—` when the api reports no cost. An amount priced at estimated rates reads `≈$0.042`.
  A running run reads "… so far" and is brought up to date by the next refresh (it does not tick). The cell's tooltip
  gives the tokens, whether the price is an estimate, and the cost cap when there is one ("of $0.50 budget").
- The cost is the agent's model calls only: the coverage runner's build and test time is not money and is not in it.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `a2a-observability`: the test-generation agent overview lists each recent run's tokens, cost, whether the cost is
  an estimate, and its budget.
- `web-ui`: the A2A screen's recent runs show a cost next to the duration.

`test-generation-runs` already requires every run to carry its tokens and cost; it does not change.

## Impact

- Code: `src/Maf.Lab.Api/Coverage/TestAgentOverview.cs` (four DTO fields, the estimate flag from the configured
  allowlist), `web/src/coverage/format.ts` (a `cost` formatter), `web/src/api/types.ts`,
  `web/src/admin/TestAgentSection.tsx` and its fixtures. Tests in `tests/Maf.Lab.Tests` and `web/src`.
- API: additive fields on each `recent` item; no new route, no parameter, no schema change. Runs stay
  repository-wide; no tenant parameter is added anywhere.
- No package, model, Jev call, make target, CLI tool or load-balancer location changes. No new UI-started process:
  the cost is read with the overview, which already shows the themed progress indicator while it loads (per the
  `progress-feedback` spec, nothing new needs one).
- The Coverage page already shows a run's tokens and cost (run status line and run details) and is not changed.

## Documentation impact

- `docs/http-api.md`: the `test-agent` paragraph lists the new `recent` fields and says which price `costUsd` uses
  and what `costIsEstimate` means.
- `README.md`: the `/admin/a2a` paragraph says the recent runs show what each cost.
- CLAUDE.md, openspec/project.md, docs/shared-state.md and .github/copilot-instructions.md do not describe this table
  and are unaffected.

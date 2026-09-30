# Design

## Context

- The attempt cap lives in the contract (`TestGenRequest.AttemptLimit = 10`); the api asks for
  `TestAgentOptions.MaxAttempts` (defaults to it). The tool-round cap (40) and the test runs per attempt (2) live only
  in the agent's `TestAgentOptions` (DECISIONS §60), so the api cannot show them. The deadline (2 h) is the api's
  `TestAgentOptions.RunDeadline`; the suspected-bug cap (3) is `SuspectedBug.MaxPerRun`.
- `GET /api/coverage/models` returns each model with an `estimate` already multiplied by `MaxAttempts`, and
  `maxAttempts`. The picker cannot re-price for other limits.
- `AttemptEstimate.PerAttempt` = `(2 × fileTokens + 6 000) × 1.6` input and 4 000 output. The agent also uses it to
  decide whether the next attempt would cross the budget.
- Measured on the 13 real attempts in the api database (4 files, 1.7–3.7 KB, `glm-5.3:cloud` and the flash model):
  input per attempt median 118k (mean 125k, 95k–211k) against an estimate of 11–12.6k; output median 2.8k, mean 5.8k,
  0.7k–17.8k. Input tracks tool rounds (14–22 per attempt, about 6.5k per round; fit `43k + 4.0k × rounds`), not file
  size (r = −0.29 over this range). Cost per attempt on `glm-5.3:cloud`: $0.068–0.10 actual against $0.016 estimated.
- The runs table gains nullable columns through `DatabaseInitializer.AddMissingColumnsAsync`; no migration needed.

## Goals / Non-Goals

**Goals:**
- One source for every run limit's default and bounds, shared by the api (to serve and validate), the agent (to
  validate and enforce) and, through the api, the browser.
- An estimate grounded in measured runs that follows the limits the administrator enters.
- Every limit visible in the model step, filled with its default.

**Non-Goals:**
- Raising any limit above today's value. The editable limits can only be lowered (their default is their maximum).
- Making the deadline or the suspected-bug cap editable per run; they are shown, not changed.
- Per-tool limits (lines read per call, bytes per write): tool mechanics the model is told about, not run limits.
- Fixing the unrelated "budget-stopped run stores attempt 0" observation (reported separately).

## Decisions

1. **`RunLimits` in `Maf.Lab.TestGen`.** A static class next to the request: `Attempts` (1–10, default 10),
   `ToolRoundsPerAttempt` (1–40, default 40), `TestRunsPerAttempt` (0–2, default 2), each a small
   `LimitBounds(Min, Max, Default)`. `TestGenRequest.AttemptLimit` stays as an alias of `RunLimits.Attempts.Max` so
   existing callers and tests compile. Alternative: keep the values in each service's options — rejected, that is how
   the api came to be unable to show them.
2. **The task carries the limits.** `TestGenRequest` gains `int? ToolRoundsPerAttempt, int? TestRunsPerAttempt`
   (appended, defaulted to null, so a request without them is valid and takes the defaults). `Problem()` checks them
   against `RunLimits`. The agent's `MaxToolRoundsPerAttempt` / `MaxTestRunsPerAttempt` options are removed; the
   handler, the nudge, `MaximumIterationsPerRequest`, the instructions and `TestAgentTools` read the request's values.
   The `run_tests` tool description stops saying "at most twice" and defers to the attempt's instructions, which state
   the number. This supersedes DECISIONS §60's "only in `TestAgentOptions`"; a new DECISIONS entry records it.
3. **Start request.** `StartRunRequest` gains `RunLimitsInput? Limits` (`MaxAttempts`, `ToolRoundsPerAttempt`,
   `TestRunsPerAttempt`, all nullable). `TestGenRuns.StartAsync` resolves each (absent → default; api's configured
   `MaxAttempts` is the attempts default, capped by the contract), rejects out-of-bounds as
   `StartOutcome.Invalid("limits", …)` → `400`, stores `MaxAttempts` (existing column) plus new nullable
   `ToolRoundsPerAttempt` and `TestRunsPerAttempt` columns, and puts them on the request. `RunSummary` gains
   `Limits { maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt }`, reading null columns as defaults.
4. **Models response.** `ModelsDto(Models, Limits, Estimate)`:
   - `limits`: `{ maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt }` each `{ min, max, default }`, plus fixed
     `deadlineMinutes` and `maxSuspectedBugs`;
   - `estimate` (null when the file cannot be read): `{ fixedInputTokens, inputTokensPerRound, typicalRounds,
     outputTokensPerAttempt }` for this file — the model-independent parts of the formula;
   - `ModelDto` loses `estimate`; prices are already on it.
   `maxAttempts` and per-model `estimate` go; the web app is the only client and changes in the same commit.
5. **The estimate formula** (`AttemptEstimate`, constants fitted on the 13 attempts above):
   - input per attempt = `43 000 + 4 × fileTokens + 4 000 × min(rounds, 20)`;
   - output per attempt = `6 000` (the mean; the median understates attempts that write whole files);
   - run estimate = attempts × per attempt, priced at the model's rates.
   `typicalRounds = 20` because measured attempts used 14–22 rounds and never approached 40; lowering the cap below 20
   lowers the estimate, raising it above does not inflate it past what runs do. Check: 3.7 KB file, glm, 20 rounds →
   127k in + 6k out = $0.089 per attempt (measured $0.084 on the accepted run). The `4 × fileTokens` term is a
   conservative placeholder — the data cannot fit it; it keeps large files from looking free.
   The browser computes the same formula from the served parts, so it follows edits without a round trip. One shared
   example (3.7 KB, glm, defaults → $0.089/attempt, $0.89 for 10) is pinned in both the xUnit and the Vitest tests so
   the two cannot drift. The agent's "would the next attempt cross the budget" check uses the same per-attempt
   figure with the task's round limit.
6. **Picker UI.** `ModelPicker` renders three fieldsets: Model, Limits (attempts, tool rounds, test runs — number
   inputs filled with defaults, with "from min to max" hints; target, deadline and suspected bugs as a read-only list)
   and Budget (each cap a checkbox "Unlimited", checked by default, and a field disabled while checked; unchecking fills
   the field with the estimate for that dimension: total tokens or cost for the current model and limits). Parsing and
   bounds checks live in a new `web/src/coverage/limits.ts` beside `budget.ts`; `BudgetInput` gains the two
   `unlimited` flags. The dialog's Start stays disabled while any limit or cap is invalid.

## Risks / Trade-offs

- **13 attempts on small files.** The constants may not hold for large files or other models. Mitigation: the
  estimate stays labelled as one, DECISIONS records the data, and the constants live in one place.
- **Lowering tool rounds may starve an attempt** (the reason §60 raised them). Accepted: the default stays 40 and the
  administrator sees the value; the field's hint says what it bounds.
- **Wire change to `testgen.request/v1`.** Additive, optional fields; an older agent would ignore them (System.Text.Json
  ignores unknown members) and apply its own 40/2 — the same as the defaults. Both deploy together here anyway.
- **Formula duplicated in TypeScript.** Mitigated by the pinned shared example in both test suites.

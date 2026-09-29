# Design

## Context

- `SystemPrompt` reads `Agent:SystemPrompt`, which defaults to `system.v2`, from `src/Maf.Lab.Api/Prompts/`. v2 has
  Tools, Examples, Scope and Rules sections. §44 shows the pattern for a prompt change: a new version, the old one kept,
  and evals run against the baselines.
- The eval host runs the real agent in-process and returns `TurnResult` (answer, tool calls, sources). After
  `add-activity-cards` the turn state also holds the cards emitted, which `TurnResult.Cards` exposes.
- `RubricJudge` already calls the chat model with a rubric and parses JSON.

## Goals / Non-Goals

**Goals:** shorter answers that interpret rather than repeat; no computed figures; measured, with a rollback path.

**Non-Goals:** no change to tool selection wording, and no answer templates. Answers in general may keep using markdown
where no card exists; `add-markdown-rendering` (planned) renders that.

## Decisions

1. **A small delta over v2.**
   - A new `## Data cards` section, before Rules. It keeps the v2 text unchanged, so selection behaviour does not move.
   - Two examples join Examples:
     - "Препоръчай ребалансиране за A-1043" → `get_household_portfolio`; answer in 2–3 sentences, no table;
     - "Does A-1042 need rebalancing?" → `get_household_portfolio`; name the drifted class and the plan's trade.
   - The section names the three card tools explicitly: a rule tied to tool names is one the model can apply.

2. **Deterministic metrics first:**
   - `noTable` checks the answer for two or more lines matching `^\s*\|.*\|\s*$`.
   - `noRowList` checks for three or more list lines, each naming a different row of a card (asset class, account id,
     quarter end). A table without pipes is still the table. It was added after the first live check, which answered
     with one bullet per class.
   - A question that explicitly asks about every row (p-05: "how far is each class from its target?") may be answered
     row by row. That was the owner's call after v3 listed p-05's classes in 2 of 4 runs. Such a row is marked
     `rowsRequested` and left out of `noRowList`.
   - `figuresGrounded`:
     - it extracts amounts from the answer (digits with space, comma, dot or NBSP grouping, next to `$`, `USD`, `лв`
       or the card's currency);
     - it normalises them to decimals and checks membership in the set of every number in the turn's card content,
       including absolute values, since "sell 8,000" is −8,000 in the plan;
     - percentages are not counted: they are compared to weights with 1 dp and are too easily restated legitimately.
   - `verdictCorrect` is the only judged metric: one `RubricJudge`-style call with "Does the answer say a rebalance is
     needed? yes/no".

3. **Dataset:** about 8 rows, EN and BG, firm-a and firm-b.
   - A-1043, within tolerance, as "needs? no".
   - A-1042, the one seed account outside tolerance (see the existing test at
     `PortfolioDomainTests:56`), as "needs? yes".
   - An AUM history question and an accounts question, for `noTable` only.
   - Thresholds: `noTable` ≥ 0.9, `noRowList` ≥ 0.9, `figuresGrounded` ≥ 0.95, `verdictCorrect` ≥ 0.9,
     `languageMatch` ≥ 0.9.
   - `languageMatch` was added after the live check answered a Bulgarian question in English in 2 of 3 tries. v2 had
     no language rule outside Scope, and v3's Bulgarian example with an English gloss tipped it over. v3 now says to
     answer in the question's language.

4. **Default switch:** `SystemPrompt` defaults to `system.v3`; the compose and eval hosts inherit it. `system.v2`
   stays, and `Agent:SystemPrompt=system.v2` rolls back.

## Risks / Trade-offs

- [The model drops useful detail when told not to repeat] → Examples show the shape: drifted class, verdict and one
  plan figure. `generation` must not regress.
- [Amount extraction misreads years or account ids] → Only numbers next to a currency marker count; ids like A-1043
  have a letter prefix.
- [Judge noise on `verdictCorrect`] → A yes/no rubric on a plain statement. Runs are repeated three times before the
  baseline is accepted.

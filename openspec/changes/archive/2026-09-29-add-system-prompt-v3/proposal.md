# Proposal

## Why

With `add-activity-cards`, portfolio data appears in the chat as a card the moment the tool returns. The system prompt
(`system.v2`) knows nothing about that, so the model still writes the same data out again as a markdown table. That
wastes the user's attention and tokens, and it is the one part of the answer where the model can get a number wrong.

With `add-rebalance-plan`, the figures the model needs are in the tool result. The prompt should say to quote them and
never compute its own.

## What Changes

- **`system.v3`** is `system.v2` plus a short "Data cards" section and two portfolio examples:
  - the results of `get_household_portfolio`, `get_aum_history` and `list_my_accounts` are already shown to the user
    as a table, so the answer does not repeat them as a table or row by row;
  - the answer says what matters: which class drifted, whether a rebalance is needed, and what the plan does, quoting
    the plan's figures;
  - it never calculates trades or weights itself;
  - when `rebalanceNeeded` is false, it says that no rebalance is needed.
- `Agent:SystemPrompt` defaults to `system.v3`; `system.v2` stays and rolls back by configuration.
- **A new eval suite, `presentation`** (`evals/presentation.jsonl`, `make eval SUITE=presentation`), over portfolio
  questions in English and Bulgarian. It is deterministic where it can be:
  - `noTable`: the share of carded turns whose answer contains no markdown table;
  - `noRowList`: the share of carded turns whose answer does not list the card's rows one by one;
  - `figuresGrounded`: the share of the answer's currency amounts that appear in the turn's card content;
  - `verdictCorrect`: the share of rebalance questions whose answer says a rebalance is or is not needed as the plan
    says. This is judged by the existing rubric-judge client with a yes/no rubric.
- The existing suites must hold against their baselines: `selection`, `generation` and `injection`.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `chat-agent`: answers do not restate data a card shows, and quote the plan instead of computing.
- `eval-harness`: the `presentation` suite and its metrics.

## Impact

- `src/Maf.Lab.Api/Prompts/system.v3.md` is new, and `SystemPrompt`'s default changes.
- `src/Maf.Lab.Eval`: `PresentationSuite`, the dataset, the Makefile target, and `TurnResult.Cards` (the cards a turn
  emitted, from `add-activity-cards`).
- `evals/baseline.json` gains `presentation` once accepted.
- **Depends on** `add-rebalance-plan` and `add-activity-cards`.
- No packages. Tool selection and routing are unchanged.

# Tasks

## 1. The presentation suite (before the prompt, so v2 has a measured starting point)

- [x] 1.1 Expose `TurnResult.Cards` (the activity type and content of each card the turn emitted) and add `PresentationSuite`, which computes `noTable`, `figuresGrounded` and `verdictCorrect` as in design §2. Add unit tests for the table detector and the amount extractor: "268 000 $", "8,000 USD", "−1 000 $", "A-1043" not counted, a year not counted. Verify that the tests pass.
- [x] 1.2 Add `evals/presentation.jsonl` (design §3), wire `--suite presentation` into `Program.cs` and `all`, add a `make eval-presentation` target and the thresholds. Verify that `make eval SUITE=presentation` runs against `system.v2` and record its numbers as the "before".

## 2. The prompt

- [x] 2.1 Add `src/Maf.Lab.Api/Prompts/system.v3.md` (v2 plus the Data cards section and two examples, design §1) and make it the default in `SystemPrompt`. Verify with a unit test that the default is v3 and that `Agent:SystemPrompt=system.v2` still loads v2.

## 3. Measurement

- [x] 3.1 Run `make eval SUITE=presentation` three times on v3, then `selection`, `generation` and `injection` once each. Verify:
  - `presentation` meets its thresholds;
  - the other three hold their baselines.

  Accept the `presentation` baseline (`make eval-accept SUITE=presentation`).
- [x] 3.2 Run `make test` and `make lint`; green. At http://localhost:7171/chat, ask "Препоръчай ребалансиране за A-1043" and verify that the card shows and the answer is 2–3 sentences with no table, saying no rebalance is needed.
- [x] 3.3 Add a DECISIONS entry: before and after numbers, the rollback, and why the metrics are deterministic. Update the README's prompt mention if it names v2. Then run `openspec validate add-system-prompt-v3 --strict`; valid.

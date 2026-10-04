# Proposal

## Why

`RubricJudge`, the one-request `gpt-oss:120b` grader that adopt-meai-evaluation replaced for `generation`
(DECISIONS.md §79), survives only in the `graph-depth` comparison's end-to-end layer. That leaves two judges with two
scales in one harness. The rubric is also the noisier and broader one: §79 measured its faithfulness range at 0.104 over
five runs, and it cannot say which claim is unsupported.

The rubric also scored faithfulness against the case's labelled needed items, not against what the turn read. So
"faithfulness" in `graph-depth` meant something different from the same metric in `generation`.

## What Changes

- The `graph-depth` end-to-end layer grades each answer with the Jev grade, the same `JevGrader` as `generation`:
  - `faithfulness` is claim sentences supported by what the turn read, with cited places checked in code;
  - `relevance` is whether the answer addresses the question.
  Neither is scored against the labelled items. Whether the answer names those items stays `mentionRecall`, a match in
  code, unchanged.
- A case fails end-to-end when faithfulness is below 0.75, relevance is not 1, or no graph tool was called (as in
  `generation`). A judge failure is still counted apart and scored 0.
- The end-to-end layer needs the Jev key instead of the chat model as a judge; the agent still needs the chat model.
  The structural layer still needs neither.
- `RubricJudge`, `GenerationSuite.RubricPass` and `GraphDepthSuite.NeededContext` are removed.
- **BREAKING** for `graph-depth` readings only. Its faithfulness and relevance change meaning, so runs before and after
  are not comparable. It is a comparison: never gated, never in the baseline.

No Jev call is added: the end-to-end layer uses the existing grade request unchanged. No make target changes.
`make eval-graph-depth` keeps its progress bar.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `eval-harness`: the Graph depth comparison requirement grades the end-to-end layer with the Jev grade instead of
  "the fixed rubric judge, scored against the case's needed items".

## Impact

- Code: `src/Maf.Lab.Eval/Suites/GraphDepthSuite.cs`, `Program.cs` (`RunGraphDepthAsync`), `Suites/RubricJudge.cs`
  (deleted) and `Suites/GenerationSuite.cs` (`RubricPass` removed).
- Tests: `tests/Maf.Lab.Tests/GraphDepthEvalTests.cs`.
- Cost: one Jev request per case and variant (24 × 3), instead of one chat-model request each.

## Documentation impact

- `README.md`: the graph-depth paragraph says "rubric scores" for the end-to-end layer. It becomes the Jev grade.
- `DECISIONS.md`: a short section recording the switch and that graph-depth readings before it are not comparable.
- `CLAUDE.md`, `docs/*.md`, `openspec/project.md`, `.github/copilot-instructions.md`: not affected.

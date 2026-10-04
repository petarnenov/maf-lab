# Proposal

## Why

graph-depth's end-to-end scores average over every turn. In a turn that never calls `trace_code_symbol` or
`change_impact`, the pinned depth has no effect, yet it still moves the variant's faithfulness and mention recall. After
route-structural-code-questions about a fifth of the turns are still like that (graphToolCalled 0.75–0.79), so the
end-to-end comparison of 2, 3 and 4 is diluted by turns that depth cannot touch.

Which turns call the graph also differs between variants and between runs. A variant scored over its own graph turns is
therefore scored on different questions from the next variant.

## What Changes

The comparison stays as it is, and the end-to-end layer reports, beside its all-turn numbers:
- **Each variant over its own graph turns.** Faithfulness, relevance and mention recall over only the turns of that
  variant that called a code graph tool, together with how many turns that was.
- **Over the common cases.** The same three scores over only the cases whose turns called a code graph tool in every
  variant, so the depths are compared on the same questions, together with how many cases that was.
- **The common cases by required depth.** Mention recall over the common cases, split by the depth each case needs, so
  a gain for deep cases is visible.
- **Per-case detail in the report.** Each case's tool-call outcome per variant, so a reader can see why a case left the
  common set.
- **The suite remains a comparison.** No thresholds, no baseline, not part of `all`. The README already asks for two
  runs before an end-to-end difference is read as a result, and that stands.
- **CLI.** No new flags; the existing progress bar is unchanged.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `eval-harness`: the graph-depth comparison's end-to-end layer also reports its scores over each variant's graph
  turns and over the cases that called the graph in every variant, with the counts behind them.

## Impact

- `src/Maf.Lab.Eval/Suites/GraphDepthSuite.cs`:
  - the end-to-end answers of every variant are kept until all variants have run, so that the common cases can be found;
  - the new metrics are computed and added to each variant's result.
- `tests/Maf.Lab.Tests/GraphDepthEvalTests.cs`: metric math over fake turns.
- No dataset, configuration, package or service change. No Jev call is added or changed.

## Documentation impact

- `README.md`: the graph-depth paragraph in "Evals — when you must run them" names the graph-turn and common-case
  scores and says to read depth effects from the common cases.
- `CLAUDE.md`, `docs/*.md`, `openspec/project.md`, `.github/copilot-instructions.md`: not affected.

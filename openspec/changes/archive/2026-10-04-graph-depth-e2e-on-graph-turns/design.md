# Design

## Context

See proposal.md for why. Today `GraphDepthSuite.RunAsync` loops over the variants and, for each, runs the structural
layer and then `EndToEndAsync`, and turns that variant's answers into metrics with `EndToEndMetrics` before moving to
the next variant. Each answer is a `(TurnResult?, JudgeScore, bool)`, and `TurnResult.ToolCalls` names the tools the
turn called. A metric name may carry a qualifier after a colon (`recall@5:bg`, `faithfulness:graph`).

## Goals / Non-Goals

**Goals:**
- Compare the depths' answer quality on turns where depth could act, and on the same questions.

**Non-Goals:**
- **Repeating runs inside the suite** (`--repeat N`). Two separate runs, as the README says, keep the cost visible and
  optional.
- **Statistical tests.** With about 24 cases, the counts beside the scores are what keep a reader honest.
- **Changing which turns call the graph.** That was route-structural-code-questions.

## Decisions

### D1. Keep every variant's answers, then compute
`RunAsync` keeps each variant's end-to-end answers, indexed by case, and only after the last variant has run computes
the common cases (graph called in every variant) and the new metrics. It then adds them to each variant's metrics before
the `EvalVariantResult`s are built.

The per-variant progress lines stay where they are. The common-case summary is printed once, after the last variant.
Keeping the answers is cheap: 24 cases × 3 variants of a `TurnResult`.

*Alternative considered:* computing only per-variant graph-turn scores. It was rejected as the only measure because
each variant would be scored on different questions. It is still reported, because its count shows how often a depth
gets the chance to matter.

### D2. Names
- `faithfulness:graph`, `relevance:graph`, `mentionRecall:graph`, `graphTurns`.
- `faithfulness:common`, `relevance:common`, `mentionRecall:common`, `commonCases`.
- `mentionRecall:common@needsK` for each required depth K that has common cases.

Absent rather than 0 when the set is empty, so an empty set cannot read as a collapse.

### D3. Per-case detail
The variant's failure reason already says `graph=called|not called`. A case that is not in the common set gets one
extra line in each variant's failures: `not common: graph not called in depth-N`. The report then shows why the set is
the size it is, without a new report field.

### D4. The "graph called" test
A turn called the graph when `TurnResult.ToolCalls` contains `trace_code_symbol` or `change_impact`, the same test
`graphToolCalled` uses. The two counts can therefore be reconciled: `graphTurns = graphToolCalled × cases`.

## Risks / Trade-offs

- **[The common set is small, so one case moves a score a lot.]** → The counts are printed beside the scores, and the
  README says to read depth effects from the common cases over two runs.
- **[The common set differs between runs.]** → That is the measurement: it is reported per run, and its count shows the
  overlap.

## Results (two runs, 2026-10-04)

Reports: `20261004-104507-graph-depth` and `20261004-110602-graph-depth`. Each cell is run 1 / run 2.

| | depth-2 | depth-3 | depth-4 |
|---|---|---|---|
| graphTurns = commonCases | 18 / 19 | 18 / 19 | 18 / 19 |
| faithfulness:common | 0.49 / 0.46 | 0.56 / 0.53 | 0.63 / 0.64 |
| relevance:common | 0.64 / 0.66 | 0.72 / 0.75 | 0.79 / 0.83 |
| mentionRecall:common | 0.78 / 0.75 | 0.76 / 0.86 | 0.87 / 0.93 |
| mentionRecall:common@needs4 | 0.48 / 0.53 | 0.57 / 0.63 | 0.85 / 0.80 |

- **The same cases call the graph in every variant** (graphTurns = commonCases). Routing is deterministic, so the
  depths are compared on the same questions.
- **The order is the same in both runs.** Faithfulness and relevance rank depth-4 above depth-3 above depth-2.
- **depth-4 beats depth-2 by more than a run's spread.** Faithfulness is +0.14/+0.18 and relevance +0.15/+0.17, while
  the same metric moves 0.02–0.10 between runs. The largest gain is on cases that need four calls (+0.37/+0.27).
- **This is the evidence for the depth-choice change.** That change also has to weigh depth-4's ~2× tokens and its
  12.5% truncation (structural layer).


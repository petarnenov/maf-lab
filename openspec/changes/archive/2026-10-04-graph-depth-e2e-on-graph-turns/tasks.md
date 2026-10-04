# Tasks

## 1. Metrics

- [x] 1.1 Add `GraphDepthSuite.GraphTurnMetrics(cases, answersByVariant)`. It returns, per variant:
      - the `:graph` scores and `graphTurns`;
      - the `:common` scores, `commonCases` and `mentionRecall:common@needsK`.

      Scores over an empty set are absent. Verify with unit tests over fake `TurnResult`s:
      - a variant with 2 of 3 graph turns scores over those 2;
      - a case missing the graph in one variant is not common for any variant;
      - no common case → `commonCases = 0` and no `:common` keys;
      - `graphTurns` equals `graphToolCalled × cases`.
- [x] 1.2 Keep each variant's answers in `RunAsync`, compute 1.1 after the last variant, merge into each variant's
      metrics, and add the `not common: graph not called in depth-N` failure lines (D3). Print one common-case summary
      line. Verify with a unit test over fake answers that the merged result holds the new keys for all three variants.

## 2. Run

- [x] 2.1 Run `make eval-graph-depth` twice and record per variant: `graphTurns`, `commonCases`, and the `:common`
      scores of both runs. Verify that both reports are in `evals/reports/` and that the numbers and their spread are in
      the PR description.

## 3. Documentation

- [x] 3.1 README.md: in the graph-depth paragraph of "Evals — when you must run them", name the graph-turn and
      common-case scores and say to read depth effects from the common cases, over two runs.
- [x] 3.2 Run `make docs` and `make docs-check`. Verify that both pass.

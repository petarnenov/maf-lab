# Tasks

## 1. Depth pin on the codebase server

- [x] 1.1 Add `GraphDepthPin` (int?, default null) to `CodeSearchOptions`. Validate on start that it is within
      1..`CallTrace.MaxDepth`. Verify with a unit test: 0 and 5 stop the host with an error naming the setting and the
      range, and null and 1–4 start it.
- [x] 1.2 Read the pin in `CodeGraphTools`:
      - trace uses `pin ?? depth ?? 2`, and skips the 1–3 argument check while pinned;
      - impact uses `pin ?? ImpactDepth`.

      Verify with unit tests against a substituted `IGraphReader`:
      - unset, depth 4 → error "between 1 and 3";
      - pinned 4, depth 2 → `CallTrace` with depth 4, and the result's depth is 4;
      - pinned 2 → impact's `CallTrace` has depth 2;
      - unset → impact depth is 4.
- [x] 1.3 Build the trace description from one template with the depth phrase as its only variable ("up to 3" /
      "up to N"). Keep `TraceDescription` equal to the unpinned text. When pinned, have `Program` replace the trace
      tool's `ProtocolTool.Description` and remove `depth` from its `InputSchema` (D1a). Verify with tests over
      `tools/list` on an in-process server:
      - pinned at 4: the description says "up to 4 calls" and names no other limit, and the schema has no `depth`;
      - unpinned: description and schema are byte-identical to the current ones;
      - the pinned and unpinned texts differ only in the depth phrase;
      - the `change_impact` description and parameters contain no number of calls.
- [x] 1.4 Add a unit test asserting that no deployed configuration sets the pin (`compose/docker-compose.yml`, every
      `appsettings*.json` under `src/`). Verify it fails when a `CodeSearch__GraphDepthPin` line is added to compose.

## 2. Dataset

- [x] 2.1 Add the `GraphDepthCase` record (id, kind, symbol, direction, path, question, language, needed items with
      hops, reference) and `DatasetLoader.GraphDepth`. Reject a row without needed items, naming the row. Verify with a
      unit test over a fixture with a good row and an empty-`needed` row.
- [x] 2.2 Write `evals/graph-depth.jsonl`, about 24 cases. Write each question and its needed items from reading the
      code first (D4), then check the hop counts against the graph with `make graph` built:
      - trace cases in both directions;
      - impact cases;
      - at least four cases each needing 1, 2, 3 and 4 calls;
      - a quarter of the questions in Bulgarian.

      Verify: the loader accepts it, and a structural run reports no unresolved case.

## 3. Eval host and the suite

- [x] 3.1 Give `EvalAgentHost` an optional code-server configuration override, plus a way to start a pinned in-process
      code server alone, without the agent, for the structural layer. Verify: an integration smoke run of the suite with
      `--limit 1 --structural-only` starts and disposes three servers, and its report settings show
      `codeServer=in-process, pin=2/3/4`.
- [x] 3.2 Implement the structural layer in `GraphDepthSuite`: an MCP call per case with the eval principal's token,
      `Stopwatch` latency, `TokenCounter` over the serialized result, and an unresolved-case check. Compute the D5
      structural metrics. Verify with unit tests over canned tool results (the metric math, including `recall@needsK` and
      unresolved handling), and with a structural run against the stack's graph.
- [x] 3.3 Implement the end-to-end layer: one agent host per variant, `AskAsync` per case, and `RubricJudge` with the
      reference and the needed items as context. Compute `mentionRecall`, `graphToolCalled` and `judgeFailures`. Verify
      with a unit test of `mentionRecall` and `graphToolCalled` over fake `TurnResult`s, and with a full run on
      `--limit 3`.
- [x] 3.4 Add one `ConsoleProgress` over cases × variants (× layers), with labels of variant, layer and case id only.
      Verify with the existing `ConsoleProgress` test pattern (non-interactive writer): done/total and percentage lines,
      no question text, and a final line on success and on cancellation.

## 4. Harness wiring

- [x] 4.1 In `Program`:
      - register `graph-depth` and the `--structural-only` flag;
      - add the comparison-suite set: no `RegressionGate.Compare`, no `BaselineStore.Accept`, not in `all`;
      - fail only on a thrown error or unresolved cases.

      Verify with unit tests:
      - `all` does not include graph-depth;
      - `--accept-baseline` leaves `evals/baseline.json` without a graph-depth key;
      - a worse rerun passes.
- [x] 4.2 Makefile:
      - list `graph-depth` in the `eval` `##` help line;
      - add `eval-graph-depth` with its `##` comment.

      Verify `make help` shows both, and `make eval-graph-depth` runs.
- [x] 4.3 Confirm no Jev request is added or changed: the end-to-end turns use the existing classifier and answer
      check unchanged, so the Jev review checklist (docs/rules/jev-usage.md §7) has nothing new to review. Run the
      existing `intent` and `answer-check` suites once to show they are unaffected, including their Bulgarian cases.

## 5. First measurement

- [x] 5.1 Run `make eval SUITE=graph-depth` twice, and note the spread of the end-to-end metrics between the two runs.
      Verify: both reports are in `evals/reports/`, and a short summary goes in the PR description:
      - per variant: recall, `recall@needsK`, `meanTokens`, `truncatedRate`, faithfulness;
      - which differences exceed the run-to-run spread.

      Do not change any depth in this change.

## 6. Documentation

- [x] 6.1 README.md, "Evals — when you must run them":
      - add `-graph-depth` to the `make eval-…` list;
      - add the trigger line (code graph builder, or a trace or impact depth → `graph-depth`);
      - say that graph-depth is a comparison suite that never gates, and that end-to-end differences need two runs.
- [x] 6.2 `.github/copilot-instructions.md`: add `graph-depth` to the eval CLI's suite list.
- [x] 6.3 Run `make docs` to regenerate the `generated:make-targets` block (never by hand), then run `make docs-check`.
      Verify both pass and that the README block shows `eval-graph-depth`.

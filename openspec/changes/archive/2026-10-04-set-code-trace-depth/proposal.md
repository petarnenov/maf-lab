# Proposal

## Why

`trace_code_symbol` follows 2 calls by default and at most 3. Those numbers were never measured (add-neo4j-graph, design
D2). Now they have been. graph-depth was run twice, pinning the trace to 2, 3 and 4 calls and comparing the agent's
answers on the same 18–19 questions where every variant called the graph.

The deepest setting answers best in both runs:
- **Faithfulness:** +0.14 and +0.18 over depth-2.
- **Relevance:** +0.15 and +0.17.
- **Mention recall on questions that need four calls:** +0.37 and +0.27.

Each of these gains is larger than the run-to-run spread. The ordering depth-4 > depth-3 > depth-2 holds in both runs.

## What Changes

- **New depth and cap.** `trace_code_symbol` follows 4 calls by default and accepts a depth of 1–4 (was: default 2, at
  most 3). The model can still ask for a shallower trace.
- **The tool's text matches.**
  - Its description says "through up to 4 calls".
  - Its `depth` parameter says "1-4 (default 4)".
  - Its error for a depth out of range says "between 1 and 4".

  The description keeps the single varying depth phrase from add-graph-depth-eval, so a pinned server's text still
  differs only in that phrase.
- **What stays.**
  - The node limit stays 100, with the `truncated` flag and nearest-first order. A trace whose depth-4 reach exceeds
    100 methods (12.5% of graph-depth's cases) keeps the 100 nearest and says it was cut.
  - `change_impact` stays at 4, with its limit of 300.
  - The evaluation pin keeps its range of 1–4.
- **A test that pins the truncation.** An integration test in the graph store shows that a trace past the limit keeps
  the nearest methods first and sets `truncated`.
- **Evals.** The tool description changes, so `selection` is re-run (README rule), with `generation` beside it. Any
  move outside tolerance is investigated, not accepted.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `code-graph`: `trace_code_symbol` takes a depth of at most 4, default 4, and the evaluation pin's statement of the
  unpinned behaviour follows.

## Impact

- `src/Maf.Lab.CodeSearch/Tools/CodeGraphTools.cs`:
  - `MaxTraceDepth = 4`;
  - a `DefaultTraceDepth = 4` constant replaces the literal `2`;
  - the depth phrase becomes "up to 4 calls";
  - the parameter description is updated.
- **Tests** that assert the published text, the 1–3 error and the default: `GraphBuildAndToolTests` and
  `CodebaseSearchTests`. A new integration test for truncation goes in `tests/Maf.Lab.IntegrationTests`.
- `evals/baseline.json`: re-accepted for `selection` only if this change moves it, and named in the commit.
- No package, configuration, service or Jev change.
- **Cost.** A trace answer is about twice as long by default (graph-depth: 2307 → 4494 mean tokens). A turn that asks
  for a shallower trace pays less.

## Documentation impact

- `README.md`, `docs/*.md`, `CLAUDE.md`, `openspec/project.md`, `.github/copilot-instructions.md`: none name the trace's
  depth or default (checked with a search for "up to 3 calls", "1-3", "1–3" and "at most 3"), so none are affected.
- The tool description itself is the model-facing documentation and is updated by the change.

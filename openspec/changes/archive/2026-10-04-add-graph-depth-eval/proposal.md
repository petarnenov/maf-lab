# Proposal

## Why

The code graph's call depths are fixed numbers that nothing has measured. `trace_code_symbol` follows at most 3 calls
(default 2), and `change_impact` follows 4. Design D2 of add-neo4j-graph sets the caps without reasons, and no eval
compares one depth with another. The cost of the wrong choice is real in both directions. A shallow trace misses the
caller a question is about. A deep one fills the model's context with methods nobody asked for, and hits the
100-node limit (`truncated`). Before any default or cap moves, a run should show what 2, 3 and 4 each find and what
each costs.

## What Changes

- A new eval suite, `graph-depth`, with three variants: `depth-2`, `depth-3` and `depth-4`. Each variant runs the same
  cases with the code graph tools pinned to that depth.
- A new hand-labelled dataset, `evals/graph-depth.jsonl`. Each case is either a trace (a symbol and a direction) or an
  impact (a repository file). Each case has a question a developer would ask, and lists the methods or test files an
  answer needs, each with the number of calls at which it is reached.
- **Structural layer (no model).** Calls `trace_code_symbol` and `change_impact` directly, once per case and variant.
  It reports:
  - how much of what is needed is reached, overall and by how deep the case needs to go;
  - the share of returned nodes that were needed;
  - mean nodes and mean result tokens (what the model would have to read);
  - the truncated rate;
  - tool latency (p50/p95).
- **End-to-end layer.** The agent answers each case's question while the graph tools are pinned to the variant's
  depth. The fixed rubric judges the answer against the case's labelled facts. The report also gives the share of
  needed symbols the answer names, and how often a graph tool was called at all.
- **Pinning depth.** A new setting on the codebase MCP server, unset by default, pins both graph tools to one depth
  from 1 to 4. When it is unset, both tools behave exactly as today: the 1–3 `depth` argument, default 2, and 4 for
  impact. Only the eval's own in-process server sets it.
- What a pinned server publishes matches the pin. The description of `trace_code_symbol` says it reaches methods
  through up to N calls, the pinned depth, and the `depth` argument is removed from its input schema, because a caller can no longer choose a depth.
  Unpinned, every description and schema is byte-identical to today's.
- **A comparison suite.** `graph-depth` reports side by side but does not gate. It has no thresholds, it is not
  compared with or accepted into `evals/baseline.json`, and it is not part of `SUITE=all`. Several of its metrics
  improve as they go down (tokens, latency, nodes), which the baseline gate cannot express.
- **CLI.** `make eval SUITE=graph-depth`, plus `make eval-graph-depth`. `--structural-only` runs the first layer with no
  chat model and no Jev key. The suite shows a determinate progress bar over cases × variants (done/total and a
  percentage), per progress-feedback. When output is not a terminal, it prints plain lines.
- No Jev call is added or changed. The end-to-end turns use the intent classifier and answer check exactly as every
  other agent turn does.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `eval-harness`: adds the `graph-depth` comparison suite, its dataset and its two layers. A comparison suite is
  reported but does not gate, and is never part of the baseline.
- `code-graph`: adds the evaluation pin. It is a server setting, unset in every deployed configuration, that fixes the
  call depth of `trace_code_symbol` and `change_impact`. The tools' public contract is unchanged when it is unset.

## Impact

- `src/Maf.Lab.CodeSearch`: an options class bound from configuration, read by `CodeGraphTools` for the trace depth and
  the impact depth. It is validated at startup to be within the depths the graph templates already have (1–4). No new
  Cypher, no new template, and no change to `TenantScopedGraph`.
- `src/Maf.Lab.Eval`:
  - a new `GraphDepthSuite`, a dataset record and loader, and a way to start the in-process codebase server with a
    pin;
  - the program learns the suite, `--structural-only`, and comparison suites skipping the baseline gate and accept;
  - the suite uses the shared `ConsoleProgress` bar.
- `evals/graph-depth.jsonl`: new. `evals/baseline.json`: unchanged.
- `Makefile`: the `eval` help line lists the suite, and a new `eval-graph-depth` target.
- Runtime needs:
  - the structural layer needs Neo4j with the code graph built (`make graph`), plus Qdrant and Ollama for the in-process
    server to start;
  - the end-to-end layer also needs `OLLAMA_API_KEY` and `JEV_MAF_LAB`.
- No package version moves.

## Documentation impact

- `README.md`:
  - the `generated:make-targets` block picks up the new target and the extended `eval` line from the Makefile `##`
    comments (via `make docs`, not by hand);
  - "Evals — when you must run them" gains a line: a change to the code graph builder, or to a trace or impact depth,
    runs `graph-depth`;
  - the `make eval-…` list in that section gains `-graph-depth`;
  - the section says that `graph-depth` is a comparison suite and does not gate.
- `.github/copilot-instructions.md`: the list of suites the eval CLI runs gains `graph-depth`.
- `CLAUDE.md`, `openspec/project.md`, `docs/*.md`: not affected. They name no suite list or graph depth that this
  change makes untrue.

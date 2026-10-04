# Design

## Context

What exists today:

- **Depth caps.** `CodeGraphTools` (`src/Maf.Lab.CodeSearch/Tools/CodeGraphTools.cs`) caps the model's trace at
  `MaxTraceDepth = 3`, default 2. It runs `change_impact` at `ImpactDepth = CallTrace.MaxDepth = 4`.
- **Templates.** `CallTrace` (`src/Maf.Lab.Retrieval/Graph/GraphQueries.cs`) already accepts depths 1–4.
  `GraphTemplates` holds one constant Cypher text per depth and direction. Depth 4 for a trace therefore needs no new
  query, only permission to ask for it.
- **The eval host.** `EvalAgentHost` starts the codebase MCP server in-process (`Maf.Lab.CodeSearch.Program.BuildApp`)
  unless `Evals:CodeMcpEndpoint` names a running one. `make eval` always names one, the stack behind the load balancer.
- **Harness shape.** Suites return `EvalVariantResult`s. `Program` runs every named suite through `RegressionGate` and,
  on `--accept-baseline`, through `BaselineStore.Accept`. The gate treats a lower number as worse.
- **The judge.** `RubricJudge` scores faithfulness and relevance from (question, reference, context, answer).
- **Turn results.** `TurnResult` carries tool calls by name and summary, not the full tool output.
- **Progress.** `ConsoleProgress` (`Maf.Lab.Hosting/Cli`) is the shared bar. The eval CLI today prints `ctx.Progress`
  lines only.

## Goals / Non-Goals

**Goals:**
- Numbers that let a later change set the trace default, the trace cap and the impact depth on evidence.
- One mechanism for both layers, so the structural numbers describe exactly what the model receives.

**Non-Goals:**
- Changing `MaxTraceDepth`, the trace default or `ImpactDepth`. That is the follow-up this suite informs.
- The billing graph's depth (1–2). It is shallow by its schema, and a billing case set is a separate dataset if ever
  needed.
- Comparing graph stores (the Memgraph benchmark is a separate follow-up).
- A web view for the suite. Its reports use the existing JSON and Markdown shape.

## Decisions

### D1. Pin the depth on the server, from configuration only
`CodeSearchOptions` gains `GraphDepthPin` (int?, default null), validated on start to lie within
1..`CallTrace.MaxDepth`. `CodeGraphTools` reads it:
- **trace:** `hops = pin ?? depth ?? 2`. The 1–3 argument check is skipped while a pin is set, because the pin, not
  the argument, decides the depth.
- **impact:** `pin ?? ImpactDepth`.

No deployed configuration sets the pin (compose, appsettings). A unit test asserts this, so the setting cannot drift
into the running stack.

*Alternatives:*
- **An eval-side wrapper that rewrites the model's `depth` argument.** It cannot reach 4 without loosening the public
  cap, and it cannot touch `change_impact`, which has no depth argument.
- **A per-request pin in MCP `_meta`.** Any caller could then choose a depth the cap forbids. The depth must come from
  configuration, like the tenant comes from the principal.
- **Raising `MaxTraceDepth` to 4 for everyone.** That would decide the question before measuring it.

### D1a. A pinned server publishes what it does
A pinned server must not tell the model "up to 3 calls" or offer a `depth` of 1–3 that it then ignores. Both the
messages and the decisions have to match the depth in effect.

- **The description.** The trace description's depth sentence comes from one place, a function of the depth phrase:
  - unpinned: "through up to 3 calls", so the text is byte-identical to today's `TraceDescription`;
  - pinned: "through up to N calls" with N the pin. A trace at depth N returns every method within 1..N calls,
    so "exactly N" would be false.
- **Publishing it.** When the pin is set, `Program` applies a `PostConfigure<McpServerOptions>`. It finds
  `trace_code_symbol` in `ToolCollection` and replaces its `ProtocolTool.Description` with the pinned text. It also
  removes `depth` from `ProtocolTool.InputSchema` (properties and `required`). The SDK (ModelContextProtocol 2.2.0)
  exposes both as settable. When the pin is unset, nothing is touched.
- **A caller that still sends `depth`.** It is ignored, as the spec states.
- **The error message.** The text "depth must be between 1 and {MaxTraceDepth}" can only appear when unpinned, where
  it is true.
- **`change_impact`.** Its description and its parameter name no number of calls. A test keeps it that way, so it is
  true at any pin.
- **Selection eval.** Unpinned text is unchanged, so the selection eval and its baseline are unaffected.
- **What the end-to-end layer measures.** The model sees the pinned description. This is the description the model
  would see if that depth became the setting, which is the comparison being made.

*Alternatives:*
- **Keeping the static text and accepting the mismatch.** Rejected: the model would be told something false, and the
  end-to-end layer would measure behaviour under a false description.
- **Keeping `depth` in the schema with a pinned range (`minimum = maximum = N`).** Rejected: an argument with one
  allowed value is noise in the model's context. A caller that sends one anyway gets it ignored.

### D2. Each variant gets its own in-process codebase server
For each variant, the suite starts `Maf.Lab.CodeSearch.Program.BuildApp` on loopback with `CodeSearch:GraphDepthPin`
set to the variant's depth. It always does this, whatever `Evals:CodeMcpEndpoint` says, because the stack's server is
unpinned and a run against it would measure the model's choice, not the variant. The report's settings record
`codeServer=in-process, pin=N`.

`EvalAgentHost.StartAsync` gains an optional configuration override for the code server, so the end-to-end layer can
build one agent host per variant.

### D3. The structural layer calls the tools through MCP, not the graph reader
The structural layer connects to the variant's server with the same MCP client the agent uses, with the eval
principal's token, and calls `trace_code_symbol` or `change_impact` with the case's arguments. Calling
`IGraphReader` directly was rejected for three reasons:
- it would skip the candidate resolution and the DTO;
- its numbers would not be the tool's numbers;
- the eval process would need its own graph wiring.

Consequences:
- The tenant comes from the token, as everywhere.
- No Cypher is built or chosen by the eval.
- The token metric counts the serialized result the model would read, using the agent's `TokenCounter`.
- Latency is tool latency, including MCP, measured with a `Stopwatch` per call.

### D4. Dataset shape
One JSONL row per case. The row below only illustrates the shape: its items and hop counts are not labels.

```json
{"id":"trace-qa-callers","kind":"trace","symbol":"TenantScopedSearch.QueryAsync","direction":"callers",
 "question":"Who ends up calling TenantScopedSearch.QueryAsync?","language":"en",
 "needed":[{"item":"DocumentSearchService.RankAsync","hops":1},{"item":"SearchDocumentsTool.SearchAsync","hops":2}],
 "reference":"…labelled facts for the judge…"}
```

- **Impact rows.** `kind:"impact"` and `path`. Their `needed` items are test file paths, with the hops of their
  nearest test method.
- **`hops`.** The number of calls at which the item is first reached. `hops: null` marks an item beyond 4 calls.
- **How a case's required depth is computed.** It is the largest non-null `hops` among its needed items. Recall is
  split by required depth: `recall@needs1` … `recall@needs4`.
- **How cases are labelled.** The question and its needed items are written from reading the code: what a developer
  asking it must be told. Only then is the hop count checked against the graph. Building the needed set from depth-4
  output would make depth 4 right by construction.
- **Size.** About 24 cases: traces in both directions, impact cases, a spread of required depths (at least four cases
  each needing 1, 2, 3 and 4), and a quarter of the questions in Bulgarian.

### D5. Metrics and names
**Structural**, per variant:
- `recall`: mean per case of reached ÷ needed;
- `fullRecall`: share of cases with all needed reached;
- `recall@needsK`;
- `signalShare`: needed reached ÷ nodes returned;
- `meanNodes`;
- `meanTokens`;
- `truncatedRate`;
- `latencyP50Ms`, `latencyP95Ms`.

An item is reached when its symbol, or for impact its test file, appears in the result.

**End-to-end**, per variant:
- `faithfulness`, `relevance`: `RubricJudge`, with the case's `reference` as reference and the case's needed items
  rendered as the context. The tool output is not in `TurnResult`, and the labelled facts are the truth the answer is
  held to;
- `mentionRecall`: share of needed items named in the answer, matched on the member name, case-insensitive;
- `graphToolCalled`: share of turns with a `trace_code_symbol` or `change_impact` call;
- `judgeFailures`, reported apart.

Lower-is-better metrics are why D6 exists.

### D6. Comparison suites skip the gate and the baseline
`Program` keeps a set of comparison suites, `{ "graph-depth" }`. For these:
- `RegressionGate.Compare` is not called;
- `BaselineStore.Accept` is not called;
- thresholds are empty;
- `all` does not expand to them.

The report's `passed` is true unless the suite threw. This was chosen over teaching the gate metric directions. A
direction table would be a second place to keep in step with every metric name, for a suite whose output is read by a
person making a decision, not by CI.

### D7. Progress
The suite owns one `ConsoleProgress` sized cases × variants (× 2 when both layers run). It advances once per finished
case of a layer, and its label shows variant, layer and case id. The bar's own non-terminal mode handles CI. The
existing `ctx.Progress` summary lines stay for each variant's results.

### D8. CLI and make
- `--suite graph-depth` runs both layers. `--structural-only` skips the end-to-end layer and does not require
  `OLLAMA_API_KEY` or `JEV_MAF_LAB`.
- `make eval-graph-depth` and `make eval SUITE=graph-depth` both work. The make targets pass the stack endpoints as
  usual, and the suite ignores the code endpoint (D2).

## Risks / Trade-offs

- **[Labels drift from the code.]** A refactor renames a method, and its case silently loses recall. → When a
  structural call returns "no method named …", candidates, or "not a C# file", the case is marked `unresolved` rather
  than counted as a miss. The report lists unresolved cases apart, and any unresolved case fails the run, because the
  dataset needs fixing.
- **[A pinned description drifts from the unpinned one.]** If the two texts were written separately, the end-to-end
  layer would compare different wording, not different depths. → One template, one varying phrase (D1a). A test
  asserts the pinned and unpinned texts differ only in that phrase, and that the unpinned text equals the published
  one.
- **[End-to-end noise.]** gpt-oss answers and judge scores vary between runs. → The structural layer is deterministic
  and carries the main signal. End-to-end differences smaller than the measured run-to-run spread are not read as
  results. The design calls for two repeat runs before drawing a conclusion, and README states this.
- **[Cost.]** About 24 cases × 3 variants = 72 agent turns plus 72 judge calls per full run. → `--structural-only`
  for quick runs, and `--limit` works as in other suites.
- **[Three in-process servers.]** Each startup costs a few seconds. → Variants run one after another and each server is
  disposed before the next starts.
- **[The pin leaks into a deployment.]** → Startup validation, the unit test from D1, and no compose variable for it.

## Migration Plan

Additive. Nothing deployed changes, the baseline is untouched, and rollback is reverting the change.

## Open Questions

- Which variant's depth a later change should adopt as the default and cap depends on the first runs' numbers. That is
  the follow-up's decision, not this change's.

# Design

## Context

See proposal.md for why. In `CodeGraphTools`:
- `MaxTraceDepth = 3`;
- the default is the literal `depth ?? 2`;
- the description is `TraceDescriptionHead + TraceDepthPhrase ("up to 3 calls") + TraceDescriptionTail`, with
  `PinnedTraceDescription(n)` swapping the phrase;
- the `depth` parameter's `[Description]` says "1-3 (default 2)".

`CallTrace.MaxDepth = 4` and `CallTrace.DefaultLimit = 100`. The Cypher returns rows ordered by hops and then symbol,
and the reader sets `truncated` when it cut at the limit.

## Goals / Non-Goals

**Goals:**
- Default and cap 4, with every published text and error true for them.

**Non-Goals:**
- **Raising the node limit.** The user chose to keep 100. Truncation is reported, not hidden.
- **Changing `change_impact`.** It is already at 4, with limit 300.
- **Changing graph-depth.** It still pins 2, 3 and 4, and stays a comparison. With the cap now at the pin's maximum, a
  server pinned at 4 publishes the same text as an unpinned one, which is the point.

## Decisions

### D1. One source for the numbers
`MaxTraceDepth = 4` and a new `DefaultTraceDepth = 4`. The default is used where `?? 2` was. The const phrase
"up to 4 calls" and the parameter text "How many calls to follow, 1-4 (default 4)." stay literal: an attribute and a
const string cannot interpolate an int. A unit test asserts that both match the two constants, so a later edit to one
fails until the other follows.

### D2. The truncation test is against the real store
A unit test cannot show that the Cypher orders by hops before `LIMIT`; only Neo4j can. A new
`GraphIntegrationTests` case writes a small graph to the Testcontainers Neo4j that the suite already uses:
- one method that calls 101 methods directly;
- one of those calls one more method.

It then traces the callees at depth 2 with the default limit and asserts:
- 100 hits;
- `truncated = true`;
- every hit at hops 1, so the depth-2 method is the one cut.

### D3. Evals
`selection` is required by the README after a tool description change. `generation` is re-run beside it because a
longer trace answer can change the answers. Both run against the stack rebuilt from this branch. Earlier this session
`generation` was measured moving 0.938–1.0 on its own, so a single low run is repeated before it is called a
regression.

## Risks / Trade-offs

- **[Longer tool results in every trace turn, about 2× tokens.]** → The model can pass a smaller depth, and graph-depth
  showed the quality gain is largest exactly where depth 4 is needed.
- **[More truncated results (4% → 12.5% of graph-depth cases).]** → The `truncated` flag is in the result. The
  nearest-first order keeps what is closest, and D2's test pins that.
- **[`selection` moves because the description reads differently.]** → It is re-run; a drop beyond tolerance is fixed
  in the description, not accepted.

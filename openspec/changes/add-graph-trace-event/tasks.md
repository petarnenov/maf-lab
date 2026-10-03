# Tasks

## 1. Read path: record each read

- [ ] 1.1 Add `GraphReadLog` and `GraphReadRecord` in `src/Maf.Lab.Retrieval/Graph/GraphReadLog.cs`:
  - `Begin()` opens an `AsyncLocal` scope, and `Dispose` restores the previous one.
  - `Current` returns the open log, or none.
  - `Add` is internal and takes a lock.
  - `ToJson(instance)` returns `{ instance, tenantScope, reads }`.
  - `Attach(result, log)` sets `_meta["maf-lab/graph"]` and `_meta["maf-lab/instance"]` when the log holds a read.
  - `OutcomeOf(exception)` returns `ok`, `unavailable`, `cancelled` or `error`.

  Verify with unit tests:
  - no scope records nothing;
  - nested scopes restore the outer one;
  - a disposed scope stops recording;
  - `Attach` leaves a result untouched for a null or empty log;
  - each exception maps to its outcome.
- [ ] 1.2 In `TenantScopedGraph.ReadAsync`, append one record per read to `GraphReadLog.Current`, from the `finally`
  block. Record the template name, limit, rows, truncation, duration rounded to 0.1 ms, outcome and error type name,
  and the bound tenants. Leave the span and metric unchanged.

  Verify with unit tests on a substituted `IDriver`:
  - a driver that throws `ServiceUnavailableException` records `unavailable` with the type name and no message;
  - a driver that returns rows records them with their count and truncation;
  - no record is written outside a scope.

## 2. Graph tools attach their reads

- [ ] 2.1 `BillingGraphTools.TraceAsync`:
  - take `RequestContext<CallToolRequestParams>? context = null` after the cancellation token, so existing callers
    still compile;
  - open a log only when `SearchDocumentsTool.TraceRequested(context)`;
  - attach it to every result, including the not-found and unavailable errors.

  Verify with unit tests:
  - with the flag, `_meta["maf-lab/graph"]` lists `billing_neighbourhood_2` and `firm_runs`;
  - without the flag, there is no `_meta`;
  - the structured content is unchanged;
  - the entity id is not in `_meta`;
  - `context` is not in the tool's input schema.
- [ ] 2.2 Do the same for `CodeGraphTools.TraceAsync` and `ImpactAsync`. Verify with unit tests: `symbol_candidates`
  and `callers_2` for a trace, and `file_methods` and `callers_4` for an impact; no symbol, path or method key in
  `_meta`.

## 3. Api: lift and record the event

- [ ] 3.1 Add `TraceKinds.Graph = "graph"` and `TraceMeta.Graph = "maf-lab/graph"`.
- [ ] 3.2 Add `GraphTraceEvent.From(callId, tool, node)` in `src/Maf.Lab.Api/Agent/Tracing/`. It returns the title,
  the data (with `rows`, `truncated`, `durationMs` and `outcome` totals) and the duration, or null for a payload with
  no reads.

  Verify with unit tests:
  - the two-read title is `Neo4j billing_neighbourhood_2 + firm_runs · 9 rows`;
  - one row is singular, "1 row";
  - a truncated read adds ` · truncated`;
  - an unavailable read gives `Neo4j billing_neighbourhood_2 · unavailable`;
  - the duration is the rounded sum;
  - a malformed payload yields null.
- [ ] 3.3 In `ChatTurnRunner.TraceToolResult`:
  - lift `maf-lab/graph` out of the recorded result's `_meta`, removing `_meta` when it is left empty;
  - add the `graph` event right after `tool.result`.

  Verify with a `TurnTraceTests` case through `FakeToolSource.SearchMetaJson`:
  - the `graph` event follows `tool.result` with the same `callId`;
  - its duration is right;
  - neither `tool.result` nor `envelope` contains `maf-lab/graph`.

## 4. Web: timeline, retrieval view, header

- [ ] 4.1 `traceData.ts`:
  - add the `GraphData` and `GraphReadData` types;
  - make `mcpInstances` include a `graph` event's instance.

  Verify with Vitest: `mcpInstances` lists a graph event's instance.
- [ ] 4.2 Add `--kind-graph: light-dark(#c2410c, #fb923c)` to `index.css`, and make `kindColor('graph')` return it.
  Verify:
  - `kindColors.test.ts` covers `graph`;
  - a test checks that no other kind resolves to `--kind-graph`.
- [ ] 4.3 `RetrievalTab`:
  - render one graph card per `graph` event: tool, `mcp:` instance, scope, rows, Neo4j time, the outcome when it is
    not `ok`, and a reads table;
  - show the empty state only when there are no searches, no judgments and no graph events.

  Verify with Vitest:
  - a graph-only turn shows the card with both reads;
  - an unavailable event says so;
  - an event with missing fields renders "n/a" without throwing.
- [ ] 4.4 `MonitorPanel` header: show the chip "N graph reads" when the visible events have any. Verify with Vitest:
  - with the cursor before the `graph` event, there is no chip and no card;
  - at the event, both appear;
  - a fixture with no graph event shows no chip.
- [ ] 4.5 Add a graph turn to `fixtures.ts` (`graphTurn`), used by the tests above. Verify:
  - the existing monitor tests still pass;
  - the timeline renders a `graph` row with `data-kind="graph"` and the graph colour.

## 5. Checks

- [ ] 5.1 No Jev call is added or changed, so the Jev review checklist (docs/rules/jev-usage.md §7) does not apply.
  Confirm with `git diff` that no file under `Agent/Jev` or `Jev*` changed.
- [ ] 5.2 Run the unit tests (`dotnet test tests/Maf.Lab.Tests`), the web tests (`npm test` in `web`), `make lint`
  and `make specs`. Verify: all green.
- [ ] 5.3 Live check: on the running stack, ask "which households are on NW-INST-2026-083" and "who calls
  TenantScopedSearch.QueryAsync". In the monitor, check the `graph` rows, the graph cards and the header chip in both
  themes. Then stop Neo4j and check the `unavailable` title. Verify: as in the specs' scenarios.

## 6. Documentation

- [ ] 6.1 `docs/trace-events.md`:
  - add the `graph` row;
  - name `maf-lab/graph` among the `_meta` keys the `tool.result` row says are lifted out;
  - add a typical order for a graph question.
- [ ] 6.2 `docs/telemetry.md`: the `graph.read` row says the same numbers reach the turn trace's `graph` event.
- [ ] 6.3 `README.md`: in the code-graph paragraph, say that each graph tool call shows in the monitor's timeline as a
  `graph` event (Neo4j templates, rows, time).
- [ ] 6.4 Run `make docs` and then `make docs-check`. Verify: no generated block changed by hand, and the check passes.

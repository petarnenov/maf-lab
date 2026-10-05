# Design

## Context

- **Mutations.** Every `mutationFn` in the web posts something short: starting an index run, a migration, a coverage
  refresh or a test-generation run (all answer `201`/`202` with the work's id), accept/discard, labels, renames. The
  work they start already has a cancel route, and Esc already uses it (`stop-everything`).
- **The four screens.** Telemetry (Prometheus), Jev statistics (stored turn traces, no Jev call), evals (report files)
  and A2A admin (the database, and a probe of the test agent's card). Their queries already pass react-query's signal,
  so leaving aborts them; Esc does not.
- **Graph.** `TenantScopedGraph.ReadAsync` and `TenantScopedGraphMaintenance.RunAsync` run their Cypher inside
  `GraphStop.RunAsync(…, query, terminate, ct)`; each class's `TerminateAsync` runs the fixed terminate statement.
  `GraphStoreTests.FindDriverCalls` (Mono.Cecil) lists every driver call by type and calling method, with a compiler
  closure attributed to its owner type.
- **Paid calls.** Chat model calls go through `IChatClient` (`ScriptedChatClient` in tests); Jev through an
  `HttpClient` whose handler is `FakeJev` in tests. Neither fake holds a call open or reports cancellation today.

## Goals / Non-Goals

**Goals:** the spec says what the code does about mutations; Esc on the four screens; a guard for `GraphStop`; proof
that a stop cancels the model and Jev requests in flight.

**Non-Goals:** proving what Ollama Cloud or Jev bill for a cancelled request (outside this system); Esc on the coverage
model picker's probe (a cached, single short model call that already aborts on leaving).

## Decisions

**Mutations: change the spec, not the code.** Aborting a starting mutation risks exactly the cost the rule exists to
prevent — paid work with no id to stop it by. The controls are already disabled while their mutation is pending.

**The four screens use the shared hook as topology does:** `useEscToStop(query.isFetching, () =>
queryClient.cancelQueries({ queryKey }))` and `<StopHint>` while loading; a stopped read keeps the last data.

**The graph guard reads IL, like the existing one.** For each method of the two graph classes (closures included) that
calls `IDriver.ExecutableQuery`, the test requires that it is either `TerminateAsync` or a lambda whose enclosing
method calls `GraphStop.RunAsync` — the lambda is identified by its compiler name (`<Owner>b__…`) and the enclosing
method by that name's owner. It names any method that breaks the rule.

**Holding a paid call in tests.** `ScriptedChatClient` gets an optional `Hold` (a step after which the stream waits on
its `CancellationToken` and records `Cancelled`, and counts what it yielded); `FakeJev` gets an optional hold on one
call site that waits on the request's token and records it. A test then starts a chat run, waits for the hold, stops
the run by aborting the request (as CopilotKit's runtime does), and asserts the model's (or Jev's) token fired, nothing
more was yielded, and no further model or Jev request was made.

- **A run that ends with no terminal event is cancelled.** Found by the Jev stop test: RunTap recorded `cancelled`
  only when `RequestAborted` was set as the response ended, and not at all for a run stopped before its first frame, so
  a stopped run could stay `running` for every replica. Now any response that ends without `RUN_FINISHED`/`RUN_ERROR`
  is recorded `cancelled`; `RunStateTracker.Cancelled()` leaves a finished run's outcome alone.

## Risks / Trade-offs

- [A test shows the model call is not cancelled] → that is a finding; its fix belongs to this change, and the tasks say
  so.
- [The IL guard is tied to compiler naming of lambdas] → the existing guard already relies on it; a compiler change
  would fail loudly, not silently.

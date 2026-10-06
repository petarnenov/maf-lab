# Proposal

## Why

Every failing parallel run logged `following run r_… failed (ObjectDisposedException)` from `RunFollower` and, in
the monitor, `could not store run frames … ObjectDisposedException`, just after "Application is shutting down…". Both
are work that kept going past its host's stop and then wrote through services the host had already disposed. They
were logged and swallowed and never failed a test, but stop-anything requires that a stop ends work at a safe point,
not underneath it.

- `RunFollower` follows each run as a fire-and-forget `Task.Run`. `BackgroundService.StopAsync` waits only for
  `ExecuteAsync`, so a run's last writes (the run's state and the follow's audit, in its `finally`) could run after the
  container disposed the database factory and the agent client.
- `MonitorObserver`'s frames write and kept trace are awaited by the core only up to its drain timeout, then left
  running (by design: a slow observer never holds up a turn). Nothing held them back from outliving the host.
- `RunOwnerHeartbeat`'s `Dispose` cancelled its loop without waiting for it, so a beat in flight could outlive the
  Redis multiplexer.

Also found while proving this change: `TestGenRunsApiTests.A_run_is_one_trace_across_the_api_the_agent_and_the_runner`
failed whenever it ran alone. It shares no state with other tests. It waited at most 15 s (300 × 50 ms) for the
`testgen.run` span, which ends only when the agent's whole run does, and a cold run alone takes about 25 s.

## What Changes

- `RunFollower` keeps the task of every run it follows. `StopAsync` calls the base (which cancels the stopping token,
  so the sweep takes no new run and every run in hand sees the same stop), then awaits those tasks, bounded by the
  host's shutdown token. On that bound it logs how many were still ending.
- `MonitorObserver` (plugin `monitor`) keeps its writes in hand and implements `IAsyncDisposable`. The container
  disposes it before the stores it depends on, and disposing waits for those writes up to `DrainTimeout` (5 s, the
  core's drain bound). A write that arrives after that is not made, and a warning says so. The observer-host contract
  of introduce-plugins (decision 7: an in-order queue per observer, never awaited inline, a bounded drain at run end)
  is unchanged.
- `RunOwnerHeartbeat` implements `IAsyncDisposable`: it cancels its loop and waits for it, up to `StopWithin` (5 s, the
  client's own default async timeout), and it disposes its `ApplicationStopped` registration. Decision 2 (it beats
  until `ApplicationStopped`) is unchanged.
- New tests: `HostShutdownTests` (the follower's run ends before the host is disposed, and nothing logs an
  `ObjectDisposedException`; the heartbeat's dispose waits for the beat in flight) and `MonitorShutdownTests` (the
  monitor's dispose waits for its frames write and starts none after).
- No `ObjectDisposedException` is caught anywhere: the work ends before the services go.
- `A_run_is_one_trace…` waits for the `testgen.run` span to end (its `ActivityStopped`), bounded at 3 minutes only so
  that a run that never ends cannot hang the suite, instead of polling for 15 s.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None (skip_specs). This is what the `stop-anything` spec already requires of server work; no scenario's wording
changes.

## Principles

- Standards: the .NET generic host's hosted-service lifecycle (`BackgroundService.StopAsync` with the host's shutdown
  token as the bound, then `IAsyncDisposable` of the container's singletons in reverse order of creation). These are
  the platform's own stop and dispose contracts; nothing of the project's own is added.
- SOLID: each service owns the end of the work it starts (single responsibility). No caller or host learns about
  another's tasks.
- Rejected: catching `ObjectDisposedException` (it hides work that outlives its host), and `IHostApplicationLifetime`
  hooks in each service (the host's stop and dispose already order this).

## Progress

None — these are background services with no command or page of their own. A run's progress on the coverage
screen and a turn's trace in the monitor show as they do today.

## Stopping

- Key: the host stopping (SIGTERM / Ctrl+C on the api, a test's host disposed)
- Stop: the generic host's stopping token, then disposal: `RunFollower.StopAsync` ends the sweep and awaits the runs in
  hand (bounded by the host's shutdown timeout); `MonitorObserver` and `RunOwnerHeartbeat` wait for their writes in
  hand on dispose (bounded at 5 s)
- Recorded in: a followed run's lease lapses as today, and another replica takes it over
- Shown: a run's state stays what its store says. A follow cut short by the stop is recorded as `stopped`, not
  `error`

## Documentation impact

- None beyond this change. `RunFollower` moves to the coverage plugin with extract-coverage-plugin, and the fix moves
  with it.

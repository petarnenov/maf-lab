# Proposal

## Why

Three stop-anything tests lose about one full parallel run in four: `TaskCancelWatchTests.Disposed_it_stops_reading`,
`ReplicaStateTests.The_job_watch_fires_only_on_a_recorded_cancel` and `ReplicaStateTests.Running_job_keeps_its_heartbeat_fresh`.
They run the watches and the heartbeat on `TimeProvider.System` and assert after real `Task.Delay` windows (100–1000 ms),
so a loaded thread pool can starve a tick, or a heartbeat, past the window and fail a test whose code is right. The
deferral was recorded in 3b332a3.

The watches (`TaskCancelWatch`, `JobCancelWatch`) and `AdminJobRunner` already take a `TimeProvider` and drive their
`PeriodicTimer` with it, so the tests can own the clock.

## What Changes

- The three tests run on `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) and move the clock
  themselves, one tick at a time. Each step waits for the effect the tick causes (a read of the store, a heartbeat
  written) instead of a fixed delay, so the outcome no longer depends on how fast the machine is. A bounded wait
  (10 s) only keeps a broken watch from hanging the run.
- `The_job_watch_fires_only_on_a_recorded_cancel` counts the watch's reads, so "did not fire" is asserted after a read
  was looked at, not after a guess at how long that takes.
- `Running_job_keeps_its_heartbeat_fresh` moves the clock past the stale cutoff one beat at a time, each beat written
  before the next.
- New test-only package `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 (DECISIONS.md §82).
- No production code changes, and no other test changes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None (skip_specs). The `stop-anything` scenarios keep their wording: the change only makes their tests deterministic.

## Principles

- Standards: .NET's `TimeProvider` abstraction (the platform's own clock seam since .NET 8) and its official test double
  `FakeTimeProvider`, from the same dotnet/extensions family as `Microsoft.Extensions.AI`. Rejected: a home-grown fake
  clock, longer delays, retrying flaky tests.
- SOLID: the watches already depend on the `TimeProvider` abstraction, not the system clock (dependency inversion), so
  the tests substitute it without touching `src/`.
- xUnit's own pattern for asynchronous effects: wait on the observable effect, not on time.

## Progress

None: the change adds no command or page. `dotnet test` shows its own progress, as it does today.

## Stopping

None: the change adds no work to stop. The tests stop like every test, through xUnit's
`TestContext.Current.CancellationToken`, which the bounded waits link to.

## Documentation impact

- DECISIONS.md §82 (the package).

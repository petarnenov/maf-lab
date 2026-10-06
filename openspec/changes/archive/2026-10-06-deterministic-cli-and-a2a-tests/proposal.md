# Proposal

## Why

The 8-run proof of deterministic-stop-anything-tests found two more stop-anything tests that lose under load, once
each:

- `CliCancelTests.The_indexer_stops_when_told_and_says_what_to_run_again(signal: "INT")` ended with exit 130 and an
  empty stderr. Every CLI test waits a fixed 4 s before it signals the tool. On a loaded machine the tool had not yet
  installed its handlers by then, so the default SIGINT killed it (.NET reports 128 + 2 = 130) before it could say
  "Cancelled.". The same race is open to every tool and both signals.
- `A2AStreamingTests.A_dropped_stream_loses_nothing_the_resubscription_cannot_recover` saw "working" as the last
  state. Its three simulated steps (120 ms each) ran on the real clock and sometimes finished before `tasks/resubscribe`
  arrived. The resubscription then held only the completed snapshot, and the run it means to catch mid-way was over.

`FeeAdjustmentLedgerTests.Two_replicas_confirming_at_once_apply_it_once` (SQLite "cannot start a transaction within a
transaction") also failed once. It is billing's, it moves with extract-billing, and it looks like a concurrency bug
rather than timing, so it is not part of this change.

## What Changes

- `CliCancelTests` signals a tool once the tool has connected to the black hole, not after 4 s. Indexing, Eval and
  A2AProbe each install their Ctrl+C and SIGTERM handlers before their first connection, so a connection means the
  signal finds the handler. A 60 s bound only keeps a tool that never connects from hanging the run.
- `BillingAgentHandler`'s simulated steps wait on the handler's own `TimeProvider`
  (`Task.Delay(delay, time, ct)`, the BCL overload), which it already passes to `TaskCancelWatch`. Production is
  unchanged: the DI clock is `TimeProvider.System`.
- `A2AStreamingTests.A_dropped_stream_loses_nothing…` gives the host a `FakeTimeProvider` through `ApiFactory`'s
  `ConfigureTestServices` hook. It holds the clock until the resubscription has its snapshot, then moves the clock one
  step at a time until the resubscribed stream ends.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None (skip_specs). The `stop-anything` and A2A scenarios keep their wording: the change only makes their tests
deterministic.

## Principles

- Standards: .NET's `TimeProvider` abstraction and its official `FakeTimeProvider` (DECISIONS.md §82). For the CLI
  tests, POSIX signal delivery: a signal sent after the handler is installed is caught, one sent before is not.
- SOLID: the handler's steps depend on the clock it is given, not on the system clock (dependency inversion), like its
  cancel watch already did.
- Test oracles wait on an observable effect (a connection accepted, a snapshot received), never on elapsed time.

## Progress

None: the change adds no command or page.

## Stopping

None: the change adds no work to stop. The tests stop through xUnit's `TestContext.Current.CancellationToken`.

## Documentation impact

- DECISIONS.md §83.

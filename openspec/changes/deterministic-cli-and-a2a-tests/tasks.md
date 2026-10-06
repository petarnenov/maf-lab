## 1. The CLI cancel tests

- [x] 1.1 `CliCancelTests` signals the tool once the black hole has accepted its first connection (bounded at 60 s),
      not after a fixed 4 s.

## 2. The A2A streaming test

- [x] 2.1 `BillingAgentHandler`'s simulated steps wait on the handler's `TimeProvider`.
- [x] 2.2 `A2AStreamingTests.A_dropped_stream_loses_nothing…` holds a `FakeTimeProvider` until the resubscription has
      its snapshot, then advances it until the run completes.

## 3. Verify

- [x] 3.1 The full parallel `dotnet test --solution maf-lab.sln`, 1 run (the user's call; the fix removes the time
      dependency rather than shrinking it): 1816 passed, 0 failed, 1 skipped. The two classes also passed 3 times
      alone, and every test that builds `BillingAgentHandler` (51) passes.
- [x] 3.2 `openspec validate --strict --all`, `make docs-check` and the warnings-as-errors build pass on the branch tip.

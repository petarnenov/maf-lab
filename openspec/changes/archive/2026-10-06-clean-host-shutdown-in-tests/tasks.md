## 1. Work ends before its host

- [x] 1.1 `RunFollower` tracks the runs it follows; `StopAsync` ends the sweep and awaits them, bounded by the host's
      shutdown token.
- [x] 1.2 `MonitorObserver` is `IAsyncDisposable`: disposing waits for its writes in hand (bounded at 5 s) and starts
      none after.
- [x] 1.3 `RunOwnerHeartbeat` is `IAsyncDisposable`: disposing cancels its loop and waits for it (bounded at 5 s).

## 2. Tests

- [x] 2.1 `HostShutdownTests`: a followed run ends before the host is disposed, with no `ObjectDisposedException`
      logged. It fails without 1.1.
- [x] 2.2 `HostShutdownTests`: the heartbeat's dispose waits for the beat in flight.
- [x] 2.3 `MonitorShutdownTests`: the monitor's dispose waits for its frames write and starts none after.

- [x] 2.4 `TestGenRunsApiTests.A_run_is_one_trace…` waits for the run span's end, not 15 s; it passes alone 5 times.

## 3. Verify

- [x] 3.1 One full parallel `dotnet test --solution maf-lab.sln` run (the user's rule): 1822 passed, 0 failed, 1
      skipped.
- [x] 3.2 `openspec validate --strict --all`, `make docs-check` and the warnings-as-errors build pass on the branch tip.

## 1. The fake clock

- [x] 1.1 Pin `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 in `Directory.Packages.props`, reference it from
      `tests/Maf.Lab.Tests`, and record it in DECISIONS.md §82.

## 2. The three tests

- [x] 2.1 `TaskCancelWatchTests.Disposed_it_stops_reading`: the watch reads on a moved tick; after dispose, more ticks
      read nothing.
- [x] 2.2 `ReplicaStateTests.The_job_watch_fires_only_on_a_recorded_cancel`: two ticks, the second read proving the
      first was looked at.
- [x] 2.3 `ReplicaStateTests.Running_job_keeps_its_heartbeat_fresh`: the clock moves past the stale cutoff one written
      beat at a time.

## 3. Verify

- [x] 3.1 No `TimeProvider.System` or fixed `Task.Delay` window left in the three tests.
- [x] 3.2 The full parallel `dotnet test --solution maf-lab.sln`, 8 runs: the three tests never fail.
- [x] 3.3 `openspec validate --strict` and `make docs-check` pass after the rebase.

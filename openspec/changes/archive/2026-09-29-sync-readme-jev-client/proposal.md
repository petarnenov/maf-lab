# Proposal

## Why

`jev-client-reuse` (DECISIONS.md §43) changed how every service reaches Jev: one kept-alive client per process, a
warm-up request at start-up, bounded retries and a log line for every attempt, driven by five new `Jev:*` settings.
The README documents the other `Jev:*` knobs next to the intent classifier, but not these. So a reader cannot tell
why each service sends a Jev request at start-up, where to see Jev's status codes, or how to turn retries off.

## What Changes

- The README's **Behind the scenes** section gains a short **Reaching Jev** paragraph:
  - one kept-alive connection shared by every Jev call in a process, recycled after
    `Jev:PooledConnectionLifetimeMinutes` (10);
  - the start-up warm-up (`Jev:WarmUp`, on; `Jev:WarmUpTimeoutSeconds`, 5): background only, skipped without a key,
    not counted in the Jev statistics;
  - retries of transient failures (`Jev:MaxRetries`, 1; `Jev:RetryDelayMs`, 100), always inside the caller's budget;
  - the per-attempt log lines and how to find them with `make logs`.
- No code, configuration or behaviour changes.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
<!-- None: documentation only (skip_specs). The behaviour is already specified in openspec/specs/jev-client. -->

## Impact

- `README.md` only.

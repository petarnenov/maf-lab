# Design

## Context

The test agent's confirmation (`TestGenerationHandler`, after a clean focused attempt that reaches the target) and the
api's verification (`RunVerifier`) send the runner the same `RunnerRequest(commit, toolchain, diff, target, "all")`. The
first comes at the end of the agent's last attempt, and the second seconds later, once the task has completed. Each is
a fresh clone, a full build and the whole suite, about 75–80 s for .NET. The runner is one replica with an in-memory
queue (`JobQueue`) that already keeps finished jobs for `KeepResultsFor` (1 h) so callers can poll them.

## Goals / Non-Goals

**Goals:**

- Never compute the same complete result twice within a short window.
- Keep the trust boundary: every result is computed by the runner.
- Make reuse visible to the caller, the api's record and the UI.
- Show an attempt's scope on its card.

**Non-Goals:**

- A cache that survives a restart or is shared between replicas.
- Caching the build separately from the tests (an incremental build is a different change).
- Making flaky tests deterministic.

## Decisions

### Where: in the runner's queue, not in a caller

Reuse lives in `JobQueue.Submit`, the one place every request passes. A caller cannot hand the runner a result, and the
api cannot be tricked by the agent: the agent never talks to the api about runner jobs. The api sends its own request,
and the runner decides from its own memory. Doing it in the api ("skip verification when the agent says it confirmed")
would trust the agent, which the verification requirement forbids.

### The key

The key is the SHA-256 of: commit, toolchain, SHA-256 of the diff (empty when there is none), target file (empty when
none), and the effective scope (`all` when none is given). These are all the request fields. Everything else that
shapes a result is fixed for the life of the process and is therefore left out of the key:

- the runner's options (test project, coverage settings, Vitest setup file, node_modules, time limit);
- the image's toolchains and the lint configuration, which come from the commit.

The store is in memory, so a restart (new image, new settings) empties it.

The commit must be a full 40-character id. An abbreviated id is resolved by git at checkout and could in principle
name something else later (a ref with that name, or an ambiguity), so such requests are neither reused nor stored. The
agent and the api always send full ids.

`Fresh` is not part of the key. It means do not reuse.

### What is reusable

A result can be reused only when it has status `ok` and `Tests.Failed == 0`:

- green results, and a failed build or lint diagnostics with all tests passing. These are determined by the input:
  compiling and linting the same files at the same commit gives the same answer;
- never `timed_out`, `error`, `checkout_failed`, `restore_failed` or `diff_rejected`. These say nothing trustworthy
  about the tests, or, for a rejected diff, are cheap and say nothing worth keeping;
- never a result with a failing test. A failure is where flakiness matters most to the caller. A cached flaky failure
  would make the agent spend an attempt fixing a test that is not broken, or fail a proof spuriously. Re-running gives
  a fresh sample. Failing results are also the case where an identical request is least likely: the agent changes the
  diff after a failure.

**Trade-off for green results (flakiness).** Verification used to be an independent second sample of the whole suite.
With reuse, a flaky test that passed in the confirmation is not sampled again. We accept this. The confirmation is
itself a whole-suite run computed by the runner on exactly that diff and commit. CI still runs the suite on merge to
`main`. Anyone who wants the second sample sets `CoverageRunner:ReuseForVerification=false`, and the api then sends
`fresh: true`.

### Single-flight

A second identical request while the first is queued or running gets its own job id that follows the first: it shows
the leader's state and queue position. When the leader finishes:

- with a reusable result: every follower is done with that result, marked reused;
- otherwise: each follower becomes an ordinary queued job, keeps its original order, and is written to the channel. It
  then runs once the queue reaches it.

A `fresh` request is never a leader for others: it does not register as in flight, so nobody attaches to it. That
keeps "fresh" meaning an independent sample. Its reusable result still replaces the stored one.

Followers get their own ids, rather than the leader's, so each caller's view is its own job, and the `reused` marker
can be applied to the follower only. A lock (`_gate`) guards the in-flight map, the reuse store and the follower lists.
All operations under it are O(n) in small n.

### Window and bound

- `Runner:ReuseResultsFor`, default 15 minutes. That is long enough to cover the confirmation→verification gap (seconds
  to minutes) and the `run_tests`→measured-run gap within an attempt. It is short enough that a stored result is never
  older than the attempt it belongs to. Setting it to zero turns reuse off.
- `Runner:ReuseMaxResults`, default 16. The oldest result is evicted first. A whole-.NET result carries a Cobertura
  report of a few MB. The queue already keeps every finished result for `KeepResultsFor` (1 h), and the store holds
  references to those same objects, so it adds no copies.

### Visibility

- `RunnerResult.ReusedFrom: { jobId, completedAt }` is set only on a reused answer. `DurationMs` stays the original
  run's.
- Logging: `runner reuse {Outcome} toolchain={Toolchain} scope={Scope}`, with outcome `hit`, `joined`, `miss` or
  `fresh`. The counter `maf.runner.reuse{outcome}` sits on the lab's `Meter`. Neither carries the commit, the diff or
  any path.
- The api stores `TestGenReport.Verification = { scope, tests, pct, reusedFrom? }` in the run's report JSON, which
  needs no schema change. It also logs `run {RunId} verified … reused={Reused}`, and the candidate panel shows one
  line about it.

### Attempt scope (D)

`AttemptRun(Scope, Files, Tests, Reason?, Reused)` is a small DTO. `AttemptActivity` and `AttemptLog` gain an optional
`Run` (the focused, or whole, measured run of the attempt) and an optional `Confirmation` (the whole-suite run, when it
ran). Both are nullable and omitted when null (`TestGenKinds.Json` ignores nulls), so stored older rows deserialize
unchanged.

The agent fills them in:

- `Files` is `Selection.TestFiles.Count`, which the runner caps at 50;
- `Tests` is passed + failed + skipped of that run;
- `Reason` is the runner's fallback reason. When the agent itself asked for the whole suite (a checkpoint without
  baseline lines), it gives that reason;
- the `Confirmation` uses the scope `all` and the confirmation's numbers.

The projection copies both into the `maf-lab/testgen-attempt` event. The web row appends the scope text, so the
existing counts (which are the confirmation's when one ran) keep their place.

## Risks / Trade-offs

- [Flaky green test sampled once instead of twice] → documented, with a bypass flag. CI runs the suite again on merge.
- [Memory] → bounded by count and by the window, and shares objects with the job table.
- [Restart loses the store] → acceptable: the cost is one extra run.
- [Single-flight follower waits for a leader that then fails] → the follower runs itself, so no failure is propagated.
- [Wrong key, a result reused for a different input] → the key covers every request field. Abbreviated commits are
  excluded. Tests vary each component.

## Migration Plan

Additive contracts only. Deploy order does not matter: an old api ignores `reusedFrom`, and an old runner ignores
`fresh`. `make up` rebuilds the runner, agent, api and web images. Rolling back is safe because no stored shape becomes
required.

## Open Questions

None.

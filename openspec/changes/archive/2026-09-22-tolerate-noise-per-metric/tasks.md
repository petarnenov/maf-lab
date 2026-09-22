# Tasks

## 1. Resolve a tolerance per metric

- [x] 1.1 Add `ToleranceFor(suite, metric)` to `src/Maf.Lab.Eval/EvalOptions.cs`, resolving the
      metric's entry, then the suite's, then `RegressionTolerance`. Keep the existing
      single-argument overload working or remove it only once nothing calls it. Verify `make lint`
      builds warnings-as-errors clean.
- [x] 1.2 Add unit tests in `tests/Maf.Lab.Tests/` for all three resolution steps, including a
      metric whose name contains `@` and `:` (`recall@5:bg`), and a suite entry that still resolves
      for metrics with none of their own. Verify with `make test-dotnet`.
- [x] 1.2a Change `RegressionTolerances` from a dictionary to a list of entries — suite, optional
      metric, tolerance, and what it was measured from — because a metric name containing `:`
      cannot be a configuration key. Migrate the existing `retrieval: 0.03` entry. Verify the
      configuration-binding test from 1.2 passes, which it does not with the dictionary shape.
- [x] 1.3 Change `RegressionGate.Compare` in `src/Maf.Lab.Eval/Reports/RegressionGate.cs` to take a
      per-metric resolver instead of a scalar, and `src/Maf.Lab.Eval/Program.cs` to pass one.
      Verify the gate's existing tests pass unchanged.
- [x] 1.4 Add a gate test for the case this change exists for: two metrics in one suite, one with a
      wide tolerance of its own and one without, where a drop passes for the first and fails for
      the second in the same comparison. Verify with `make test-dotnet`.

## 2. Measure the noise before setting any number

- [x] 2.1 Run `make eval SUITE=selection` five times in one session and record every metric's value
      per run. Verify the runs are consecutive and nothing else changed between them.
- [x] 2.2 Do the same for `retrieval`, recording each variant's metrics including the per-language
      ones. Verify `recall@5:bg` and `recall@5:en` are recorded separately, since the whole premise
      is that they differ.
- [x] 2.3 Do the same for `generation`, `injection` and `confirmation`.
- [x] 2.4 Write the spread per metric — min, max, and the range — into this change's design notes,
      including the metrics that did not move. Verify every metric each suite reports appears,
      so a metric is never left unmeasured and then given a tolerance.

## 3. Set the tolerances from what was measured

- [x] 3.1 For each metric whose measured range exceeds the default tolerance, set a tolerance for it
      in `src/Maf.Lab.Eval/eval.json` derived from that range, with a comment naming the range and
      how many runs it came from. Verify the file still parses and the run reads the values.
- [x] 3.2 Remove or narrow the existing `retrieval` suite-level tolerance of 0.03 now that the
      metric that needed it has its own, so the suite's stable metrics stop inheriting it. Verify
      `recall@5:en` is then judged against the default.
- [x] 3.3 If a metric's measured range is so wide that a tolerance covering it would also cover a
      regression worth catching, do not set one: record the range, say why no number qualifies, and
      leave that metric on the default. Verify the decision is stated against the recorded numbers.
- [x] 3.4 Run `make eval SUITE=all` three times and verify the gate passes each time, or that any
      failure names a metric whose measured range says it should have failed.

## 4. Confirm nothing else moved

- [x] 4.1 Run `make lint` and `make test` and verify both pass.
- [x] 4.2 Verify a deliberately regressed metric still fails the gate: temporarily edit a baseline
      value in `evals/baseline.json` so a metric reads as dropped beyond its tolerance, run that
      suite, confirm the run fails and names the metric, then restore the file. Verify
      `git status` is clean afterwards.

# Tasks

## 1. Baseline and comparison

- [x] 1.1 Add the baseline contract (suite → variant → metric, plus `acceptedFrom` and `acceptedAt` per suite), its reader (a missing file means "no baseline") and its writer; verify tests: a missing file reads as empty, a round trip preserves values and provenance, and the file is written in a stable key order so a diff shows only what moved
- [x] 1.2 Add the comparison as a pure function over (baseline, variant results, tolerance) returning one `MetricComparison` per metric with `regression | noise | improvement | new | missing`; verify tests for each status including a drop exactly at the tolerance (not a regression), a metric absent from the baseline (new, does not fail), and a baseline metric the run did not produce (missing)

## 2. The run

- [x] 2.1 Compare after each suite, carry `Comparisons` on the report, and make a run fail when any metric regressed as well as when a threshold is missed — printing suite, variant, metric, both values and the drop; verify tests: a regression fails the run and is printed, noise and improvements pass, thresholds still fail independently, and an older report without comparisons still deserialises
- [x] 2.2 `--accept-baseline` writes the baseline from the run it just performed (with run id and time) and is the only thing that writes it; add `make eval-accept`; verify tests: a normal run leaves the file untouched even when better, accepting records the metrics and provenance, and accepting a failing run is refused rather than blessing a regression
- [x] 2.3 `Evals:RegressionTolerance` in `eval.json` (default 0.02); verify a test that it binds and that changing it changes which drops fail

## 3. The trend

- [x] 3.1 Show, on `/evals`, a chosen suite's metric across runs as a small inline SVG with the baseline marked, and a sentence instead of a chart when there are fewer than two runs; verify tests for the chart's points, the baseline marker and the too-little-history case
- [x] 3.2 Show a run's comparison in words and figures — what regressed, what improved, what was new — never by colour alone, and tolerate a report that has none; verify tests for a regression, an improvement and an old report

## 4. Adoption, verification and docs

- [x] 4.1 Produce the first baseline from a real run (`make eval-accept`) and commit `evals/baseline.json`; verify the recorded numbers match the run's report, then re-run the suites and confirm the gate passes with no regressions
- [x] 4.2 Prove the gate: lower one metric's baseline entry and confirm the run reports an improvement; raise one and confirm it fails naming the metric and the drop; restore the file. Then run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`, and open `/evals` to see the trend with the baseline marked
- [x] 4.3 Update README (the gate, `make eval-accept`, and that the floors stay), `DECISIONS.md` (committed baseline over last-report, the tolerance and why, accepting as a separate act, new/missing as signals) and the evals section on when to re-run; verify the sections exist

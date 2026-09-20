# Design

## Context

See proposal.md for why. What it has to fit into:

- `Program.cs` runs each suite, builds an `EvalReport(RunId, Suite, StartedAt, FinishedAt, Settings, Variants,
  Passed)` where `Passed = variants.All(v => v.Passed)`, writes it through `ReportWriter`, and returns 1 if any
  suite failed.
- A variant's `Passed` comes from `SuiteContext.Variant`: every configured threshold met. Thresholds live in
  `src/Maf.Lab.Eval/eval.json` under `Evals:Thresholds`, keyed by suite.
- Metric keys are per suite and not fixed: retrieval already emits `recall@5`, `recall@20`, `mrr` and, since the
  multilingual change, `recall@5:<language>`. Variant names differ too (`hybrid`, `dense`, `agent`).
- `evals/reports/` is **gitignored**; the `/evals` screen reads it through `GET /api/evals/reports`, which returns
  `EvalReportSummary` (run id, suite, started, passed, variants).
- The web app has no chart library and four dependencies in total; the topology page draws its own SVG.

## Goals / Non-Goals

**Goals:**
- The repository states the metrics currently accepted, and a change to them is reviewable.
- A drop names itself: suite, variant, metric, both numbers, the size of the drop.
- The gate works in CI, where there is no history.
- A slow decline is visible on the screen.

**Non-Goals:**
- Replacing the thresholds. Floors answer "is this usable at all"; the baseline answers "is this worse than it
  was". Both stay, and a run must satisfy both.
- Statistics: no confidence intervals, no run-to-run variance modelling. A fixed tolerance is the honest tool for
  four suites run on demand.
- Storing eval history in the repository or a database. The screen's trend comes from the reports a machine has;
  the *gate* deliberately does not depend on them.
- Blocking pull requests. The evals workflow is manual and needs a secret; the gate's job is to fail the run that
  is asked for, which is what a person or that workflow reads.

## Decisions

### The baseline is a committed file, not the last report
`evals/baseline.json`: suite → variant → metric → value, plus, per suite, the run id it was accepted from and when.
A file in the repository is the only option that works in CI (a fresh checkout has no `evals/reports/`), shows up in
a diff when it moves, and can be reasoned about without running anything.

Alternative rejected — **compare with the newest local report**: zero maintenance, but it cannot work in CI, and it
ratchets *downwards*: each run compares with a slightly worse predecessor, so a slow slide never trips anything.
That is precisely the failure this change exists to catch.

### Comparison is by suite, variant and metric name, and an unknown metric is news
The comparison walks the run's variants. For each metric: below the baseline by more than the tolerance is a
`regression`; below it by less is `noise`; at or above is `improvement`; a metric the baseline does not mention is
`new`; a baseline metric the run did not produce is `missing`. Only `regression` fails the run.

`new` and `missing` are reported rather than ignored, because both are how a gate silently stops gating: rename a
metric and the old baseline entry goes unmatched while the new one has nothing to compare against.

### One tolerance, configurable, with the reason stated
`Evals:RegressionTolerance` in `eval.json`, default **0.02** absolute. `generation` and `selection` call a live
model and move between runs; a zero tolerance would fail on noise, and a gate that cries wolf gets disabled. The
number is deliberately one knob rather than per-metric, until there is evidence that a metric needs its own.

Alternative considered — relative tolerance (2% of the value): kinder to small metrics, harsher to large ones, and
harder to reason about for values that are all in 0..1. Absolute is honest here.

### Accepting the baseline is a separate command
`dotnet run --project src/Maf.Lab.Eval -- --suite all --accept-baseline`, wrapped as `make eval-accept`. It writes
the baseline from the run's metrics and records `acceptedFrom` (the run id) and `acceptedAt`. A run without that
flag never writes the file — otherwise the first re-run after a regression would quietly bless it.

Accepting still runs the suites: the baseline records numbers this repository actually produced, never numbers
someone typed.

### The comparison travels in the report
`EvalReport` gains `Comparisons: IReadOnlyList<MetricComparison>` (`variant`, `metric`, `baseline`, `value`,
`delta`, `status`). The screen then shows what moved without recomputing it, and a stored report explains itself
later. `Passed` becomes "every threshold met **and** no regression", so the existing exit code and the `/evals`
screen's pass column keep their meaning.

### The trend is drawn from the reports the screen already has
`GET /api/evals/reports` already returns every run's variants and metrics. The screen groups by suite and metric,
sorts by time and draws a small SVG line with the baseline as a horizontal marker — the same "no new dependency"
approach the topology page took. Fewer than two runs shows a sentence instead of an empty chart.

## Risks / Trade-offs

- **A tolerance hides a real 0.02 regression** → it does, and that is the price of a gate that does not cry wolf;
  the trend makes a succession of small drops visible, and the tolerance is configuration.
- **The baseline drifts upward through careless accepts** → accepting is a separate command whose result lands in a
  reviewable diff carrying the run id it came from.
- **A metric rename silently loses its baseline** → reported as `new` and `missing` in the same run, which is the
  signal that a rename happened.
- **The trend depends on local report history** → stated on the screen; the gate does not depend on it.

## Migration Plan

Additive: a new file, a new flag, a new field on the report (absent in older reports, which the screen tolerates).
The first baseline is produced by running the suites with `--accept-baseline` and committing the result. Without a
baseline file the comparison reports everything as new and nothing fails, so the change is inert until adopted.

## Open Questions

- Whether `injection` should have a tolerance at all, since its pass rate is 1.0 and any drop is a real failure —
  answerable once the baseline exists, and expressible with the same one knob set per suite later without changing
  the contract.

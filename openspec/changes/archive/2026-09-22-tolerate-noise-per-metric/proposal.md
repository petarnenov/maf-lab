# Proposal

## Why

The regression gate compares every metric in a suite against one tolerance. Measured today, the
metrics in a single suite do not share a noise level anywhere close to that assumption:

| suite | metric | measured spread over repeated runs | tolerance in force |
|---|---|---|---|
| retrieval | `recall@5:bg` | ~0.041 (0.660 – 0.701) | 0.03 |
| retrieval | `recall@5:en` | 0.000 (0.693 every run) | 0.03 |
| generation | `faithfulness` | ~0.094 (0.906 – 1.000, 11 runs) | 0.02 |
| generation | `sourceRecall` | 0.000 (0.875 every run) | 0.02 |

Both suites are broken by this in opposite directions at the same time.

`generation` fails its gate on roughly half of runs, because its judge scores one free-text answer
differently between identical runs and the default 0.02 cannot absorb it. A gate that cries wolf
gets ignored, and `EvalOptions` says so in its own comment.

`retrieval` already has the per-suite escape hatch — 0.03 instead of 0.02 — and it is still not
enough for `recall@5:bg`, which swung 0.034 in verification today and failed the run. Widening it
again would silence that metric at the cost of `recall@5:en`, which has not moved at all across
every run measured: a real 0.03 regression there would pass unnoticed under a tolerance sized for
its noisy neighbour.

The noise is a property of a metric, not of a suite. A non-English query goes through a live
translation model and a different wording retrieves different chunks; the English query does not.
An LLM judge scores prose; a source-id comparison does not. One number per suite is either too
loose for the stable metrics or too tight for the noisy ones, and today it manages to be both.

## What Changes

- A tolerance can be configured for one metric of one suite, not only for a whole suite. Lookup
  falls back from the metric, to the suite, to the default, so everything configured today keeps
  working and nothing has to be restated.
- The tolerances that exist are derived from each metric's measured run-to-run spread, and what was
  measured is written down beside the number. A tolerance nobody can trace back to a measurement is
  a number that will drift.
- A metric that does not move keeps the default. Stability is not something to be given away
  because a metric next to it is noisy.

The gate's behaviour changes in one observable way beyond the failures it stops raising: a stable
metric in a suite with a widened tolerance stops inheriting that width. `recall@5:en` dropping 0.025
passes today and will fail after this. That is the point — it is what the current arrangement hides.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `eval-harness`: "Reports and thresholds" — the tolerance a metric is compared against is that
  metric's, and it is derived from measured noise rather than chosen to make a run pass.

## Impact

- `src/Maf.Lab.Eval/EvalOptions.cs` — `ToleranceFor` resolves a metric before a suite.
- `src/Maf.Lab.Eval/Reports/RegressionGate.cs` — `Compare` takes a per-metric resolver instead of
  one number.
- `src/Maf.Lab.Eval/Program.cs` — passes the resolver.
- `src/Maf.Lab.Eval/eval.json` — the configured tolerances, with what each was measured from.
- `tests/Maf.Lab.Tests/` — the gate's existing tests, plus fallback and per-metric resolution.
- No change to any metric, dataset, baseline value or threshold. No dependency, no package version
  moves.

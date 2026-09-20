# Proposal

## Why

The evals protect against being *bad*, not against getting *worse*. A suite passes whenever its metrics clear the
fixed floors in `eval.json` — `recall@5 ≥ 0.6` — so retrieval could fall from 0.69 to 0.61 and every run would
still report PASSED. That is the whole space between "it works" and "it used to work better", and nothing watches it.

Two things make that worse in practice:

- **The history is not in the repository.** `evals/reports/` is gitignored, so a CI run starts with no past to
  compare against; only a laptop that happens to have run the suite before knows the previous numbers.
- **Nothing states what "good" currently is.** The numbers this project worked for — Bulgarian recall@5 at 0.681,
  selection precision at 0.913 — live in a commit message and in `DECISIONS.md` prose. A person has to remember
  them to notice a drop.

The last change proved the cost: the Bulgarian eval cases went in at 0.208 and the suite simply failed the floor.
Had they gone in at 0.58, nothing would have said a word.

## What Changes

- A **committed baseline** (`evals/baseline.json`) records the accepted metrics per suite and variant, so the
  repository states what "good" currently is and a change to it shows up in review.
- Every run **compares against the baseline** and fails when a metric drops by more than a configured tolerance,
  naming the suite, the variant, the metric, both numbers and the size of the drop. Improvements never fail.
- Moving the baseline is a **deliberate act** (`make eval-accept`), never a side effect of a run, so a regression
  cannot be laundered into the baseline by running the suite again.
- A metric with no baseline entry is **reported as new**, not silently ignored, so adding one cannot quietly opt out
  of the gate.
- The **`/evals` screen shows the trend**: each metric across past runs with the baseline marked, so a slow slide is
  visible rather than inferred.

## Capabilities

### Modified Capabilities

- `eval-harness`: "Reports and thresholds" gains the baseline comparison — what counts as a regression, what
  happens to a metric with no baseline, that improvements pass, and that the baseline only moves when asked.
- `web-ui`: the `/evals` screen requirement gains the trend and the baseline marker.

## Impact

- `evals/baseline.json` (new, committed) and `src/Maf.Lab.Eval/eval.json` (the tolerance).
- `src/Maf.Lab.Eval` — a comparison step after each suite, `--accept-baseline`, the exit code, and the report
  carrying the comparison so the screen can show it.
- `Makefile` — `eval-accept`.
- `web/src/evals/*` — the trend, from the reports the screen already lists.
- `docs/http-api.md` if the report shape grows, `README.md`, `DECISIONS.md`.
- No change to the suites themselves, the datasets or the thresholds already in place: the floors stay, the gate is
  added beside them.

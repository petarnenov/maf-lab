# Design

## Context

- **The tolerance lookup.** `EvalOptions.ToleranceFor(suite, metric)` uses a metric's own entry, then the suite's, then
  `RegressionTolerance` (0.02).
- **`generation`'s entries.** It has one entry, for `relevance` (0.035).
- **The baseline.** It is from `20261003-063144-generation`: faithfulness 1, jevGroundedAgreement 1, jevUncertain 0.167,
  relevance 1.
- **The gate has no direction.** It fails any drop beyond tolerance. For `jevUncertain`, where lower is better, a drop
  is an improvement that the gate reads as a regression. This was seen today: 0.167 → 0.083, flagged.

## Goals / Non-Goals

**Goals:**
- Each noisy `generation` metric has a tolerance taken from its own measured spread, as the spec requires.

**Non-Goals:**
- **Giving the gate metric directions.** A lower-is-better metric still reads a drop as a regression. Its tolerance
  covers its measured spread, so the gate does not fire on noise. A real direction table would be a change to
  `RegressionGate` and to the eval-harness spec, and is noted as follow-up.
- **Changing the rubric judge or the dataset.**
- **Moving the baseline.**

## Decisions

### D1. Five consecutive runs on one commit, against the rebuilt stack
The runs use the same procedure as the existing `relevance` entry: five runs, consecutive, one commit, `make eval
SUITE=generation`, against the stack built from that commit. The runs from earlier today are not reused: they span two
commits, and one of them carried a behaviour change.

### D2. Tolerance = measured range, rounded up to the next 0.005
This follows the existing entry (range 0.0312 → 0.035). It is applied to `faithfulness`, `jevGroundedAgreement` and
`jevUncertain`. The two other metrics are handled like this:
- `sourceRecall` gets an entry only if it moved;
- `jevChecked` and `jevRelevantAgreement` get none if they stayed at 1.

If the five runs happen to show a range smaller than the drops already seen today (0.083 for faithfulness), the larger
observed range is used, and the `Measured` text names both. A tolerance narrower than a spread already observed would
cry wolf again.

## Risks / Trade-offs

- **[A wide tolerance hides a real regression of that size.]** → It is the measured noise of a twelve-case suite with
  an LLM judge. A real regression shows as a drop beyond it, or as the same drop over consecutive runs. Thresholds
  still answer "usable at all".
- **[Five runs underestimate the spread.]** → D2's rule takes the larger of the five-run range and the ranges already
  observed.

# Proposal

## Why

`generation` has failed its regression gate on runs that differ from the baseline only by chance. On one commit,
faithfulness moved 0.917, 0.958, 0.958. On the previous commit it moved 0.938, 0.938, 1.0, 1.0.

The baseline is 1.0. The only tolerance `generation` has is for `relevance` (0.035, measured over five runs). So
`faithfulness` and `jevGroundedAgreement` fall back to the default of 0.02. That is less than one case of twelve
scoring 0.5, which moves the mean by 0.042.

The eval-harness spec requires a noisy metric to carry a tolerance derived from its own measured run-to-run spread,
because a stable default is "too tight for the noisy ones". These two metrics have never been measured, and the gate
cries wolf on them.

## What Changes

- **Measure.** Run `generation` five consecutive times on one commit and record, per metric, the minimum, the maximum
  and the range.
- **Set the tolerances.**
  - `faithfulness`, `jevGroundedAgreement` and `jevUncertain` get their own entries in `Evals:RegressionTolerances`,
    each set to its measured range, rounded up to the next 0.005.
  - Each entry carries a `Measured` text stating the runs, the commit and the range, like the existing `relevance`
    entry.
  - `relevance` is re-measured in the same runs and updated only if its range has grown.
- **The baseline does not move.** The point is that the gate stops failing on noise, not that the bar is lowered. The
  baseline is re-accepted only if a measured run falls below an existing threshold, which would be a finding to report,
  not to absorb.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
<!-- none: the tolerance rule in eval-harness ("Reports and thresholds") already requires exactly this. The change is
     configuration and its measurement. -->

## Impact

- `src/Maf.Lab.Eval/eval.json`: new and updated `RegressionTolerances` entries for `generation`.
- No code, dataset, baseline or package change.
- **Cost.** Five `generation` runs: about 12 agent turns and 12 judge calls each.

## Documentation impact

- `README.md`'s "Not getting worse" section describes tolerances per metric in general terms and names the
  `retrieval` example. It stays true, so no edit is needed.
- `CLAUDE.md`, `docs/*.md`, `openspec/project.md` and `.github/copilot-instructions.md` are not affected.

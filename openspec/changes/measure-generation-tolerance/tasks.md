# Tasks

## 1. Measure

- [x] 1.1 With the stack built from HEAD, run `make eval SUITE=generation` five consecutive times. Record per metric the
      minimum, the maximum and the range in this file. Verify that the five reports are in `evals/reports/` and that
      the table is complete.

## 2. Configure

- [x] 2.1 Add or update `Evals:RegressionTolerances` entries for `generation`, applying D2: `faithfulness`,
      `jevGroundedAgreement` and `jevUncertain`, plus `relevance` if its range grew, plus `sourceRecall` if it moved.
      Each entry gets a `Measured` text. Verify that the JSON is otherwise byte-identical, by reviewing the diff.
- [x] 2.2 Run `make eval SUITE=generation` once more. Verify that it passes the gate and does not move the baseline
      (`git diff evals/baseline.json` is empty).

## 3. Documentation

- [x] 3.1 Confirm that the README's "Not getting worse" section stays true, then run `make docs-check`. Verify that it
      passes.

## Measured (1.1)

Five consecutive `generation` runs on `30a65e9`, against the stack built from it. Reports `20261004-115448` to
`20261004-115842`.

| metric | runs | min | max | range | tolerance |
|---|---|---|---|---|---|
| faithfulness | 0.979, 0.979, 0.979, 1, 0.896 | 0.896 | 1 | 0.104 | 0.105 (new) |
| relevance | 1, 1, 1, 1, 0.979 | 0.979 | 1 | 0.021 | 0.035 (kept: earlier range 0.031 is larger) |
| sourceRecall | 0.917 × 5 | 0.917 | 0.917 | 0 | default |
| jevChecked | 1 × 5 | 1 | 1 | 0 | default |
| jevUncertain | 0.083, 0.167, 0.167, 0.25, 0.083 | 0.083 | 0.25 | 0.167 | 0.17 (new) |
| jevGroundedAgreement | 1, 1, 1, 1, 0.833 | 0.833 | 1 | 0.167 | 0.17 (new) |
| jevRelevantAgreement | 1 × 5 | 1 | 1 | 0 | default |

Four of the five runs failed the gate under the old tolerances, while the code did not change between them.


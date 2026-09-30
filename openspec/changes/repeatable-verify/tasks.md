# Tasks

## 1. Verify

- [x] 1.1 In `scripts/verify_lb.sh`, propose +200 on A-1042 first (ask, summary, apply, idempotent second confirmation as before), then propose and confirm −200, and add a check that the fee after the reversal equals the `currentFee` before the run. Verify: the script's checks read as described, and each write has its reversal

## 2. Data

- [x] 2.1 Restore A-1042 on this machine to its seeded 1,200 USD with one audited adjustment through `propose_fee_adjustment`, with a reason naming this change. Verify: a proposal's summary shows `currentFee` 1200

## 3. Verification

- [x] 3.1 Run `make verify` twice in a row. Verify: both runs pass, and the fee is 1,200 after each (run five times: all passed, 1200.0 before and after)
- [x] 3.2 Update the check count in README and `make help` to what `make verify` prints. Verify: the numbers match
- [x] 3.3 `openspec validate repeatable-verify --strict` passes

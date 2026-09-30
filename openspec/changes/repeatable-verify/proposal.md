# Proposal

## Why

`make verify` fails locally on "a proposal asks for input and writes nothing". Each run confirms a real −200 USD
adjustment on account A-1042, and the ledger keeps it. The seeded fee of 1,200 USD has drifted to −3,605.16 USD over
local runs, and `guard-fee-adjustment-sign` now refuses any reduction below zero. CI never sees this, because it starts
from fresh volumes. A verification that changes the data it verifies cannot be run twice.

## What Changes

- `scripts/verify_lb.sh` leaves the fee where it found it:
  - it proposes an **increase** of 200 USD first, which the sign guard always allows, and checks the ask, the summary,
    the apply and the idempotent second confirmation as before;
  - it then proposes and confirms the matching −200 USD reversal, and checks that the fee is back where it started.
- A one-off repair of this machine's ledger. A-1042 is brought back to its seeded 1,200 USD through the same audited
  tool, as an adjustment with a stated reason. Nothing is deleted from the ledger.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `make-workflow`: `make verify` must leave the stack's business data as it found it, so it can be repeated.

## Impact

- `scripts/verify_lb.sh`: the fee-adjustment block. Checks are added, and the count in README and `make help` goes up.
- The local ledger gets one corrective adjustment. No code in `src/` changes.

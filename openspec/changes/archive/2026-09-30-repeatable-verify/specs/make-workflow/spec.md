## ADDED Requirements

### Requirement: Verification can be repeated
`make verify` SHALL leave the business data of the stack it checks as it found it. A check that writes, such as a
confirmed fee adjustment, SHALL be undone by the same run through the same product path, so that running
`make verify` again against the same stack gives the same result. The order of a write and its reversal SHALL be
chosen so that neither is refused by the product's own rules while the data is in a valid state.

#### Scenario: Two runs in a row
- **WHEN** `make verify` passes and is run again against the same stack
- **THEN** it passes again

#### Scenario: The fee is back where it started
- **WHEN** `make verify` has applied its fee adjustment and the reversal
- **THEN** the account's fee equals the fee before the run

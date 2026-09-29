# Spec Delta

## ADDED Requirements

### Requirement: A reduction never takes a fee below zero
An adjustment that reduces an account's fee SHALL be refused when the fee that would result is below zero. The
rule SHALL be checked when the adjustment is proposed, against the current fee, and again when a confirmed
adjustment is applied, against the current fee at that moment, so that adjustments which each pass alone cannot
together take the fee below zero. A resulting fee of exactly zero SHALL be allowed. An adjustment that raises the
fee SHALL NOT be refused by this rule, even when the fee is below zero before and after it. A refusal SHALL say
that the fee would go below zero and SHALL write nothing. A confirmation of an adjustment that has already been
applied SHALL still be reported as already applied.

#### Scenario: Refused at proposal
- **WHEN** an account's current fee is 812 and an adjustment of -4,116 is proposed
- **THEN** the result is an error saying the fee would go below zero, no input is requested, and the current fee is still 812

#### Scenario: Down to exactly zero
- **WHEN** an account's current fee is 812 and an adjustment of -812 is proposed and confirmed
- **THEN** it is applied and the current fee reads 0

#### Scenario: Two proposals that are each fine alone
- **WHEN** two adjustments of -500 are proposed on an account whose current fee is 812, and both are confirmed
- **THEN** the first is applied, the second is refused because the fee would go below zero, and the current fee reads 312

#### Scenario: Raising a fee that is already below zero
- **WHEN** an account's current fee is -6,048 and an adjustment of +1,000 is proposed and confirmed
- **THEN** it is applied and the current fee reads -5,048

#### Scenario: A repeated confirmation after the fee has moved
- **WHEN** an adjustment of -500 was applied to a fee of 812, a later adjustment brought the fee to 0, and the first confirmation arrives again
- **THEN** it is reported as already applied with its original resulting fee, and nothing is written

# Spec Delta

## ADDED Requirements

### Requirement: The generation suite reports Jev's answer check
The `generation` suite SHALL read, for each case, the verdict and probabilities of Jev's answer check from the turn it
ran — without a Jev request of its own — and SHALL report them beside the rubric's scores: in each case's progress line
and failure reason, and as metrics of the run:
- the share of cases Jev checked;
- over the checked cases, how often Jev's grounding verdict agrees with the rubric's faithfulness passing, and how
  often Jev's relevance verdict agrees with the rubric's relevance passing.

The agreement metrics SHALL be omitted when no case was checked, rather than reported as zero. They SHALL have no
pass/fail threshold of their own.

#### Scenario: Agreement reported
- **WHEN** the generation suite runs and Jev checks every case's answer
- **THEN** the report carries the checked share and both agreement metrics beside faithfulness and relevance

#### Scenario: Jev unavailable during the eval
- **WHEN** no case's answer could be checked
- **THEN** the report carries a checked share of 0 and no agreement metric, and the rubric's metrics are unaffected

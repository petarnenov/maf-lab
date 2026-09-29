# Spec Delta

## ADDED Requirements

### Requirement: The threshold is a size, not a direction
The reviewer SHALL refuse an adjustment whose size exceeds its refusal threshold, whether the adjustment raises the
fee or reduces it. An adjustment whose size is at or below the threshold SHALL NOT be refused for its size.

#### Scenario: A large credit
- **WHEN** the threshold is 1,000 and an adjustment of -4,116 is reviewed
- **THEN** the result carries `refused` with a reason naming the threshold

#### Scenario: A large increase
- **WHEN** the threshold is 1,000 and an adjustment of +4,116 is reviewed
- **THEN** the result carries `refused` with a reason naming the threshold

#### Scenario: A credit at the threshold
- **WHEN** the threshold is 1,000 and an adjustment of -1,000 is reviewed
- **THEN** the result is not refused for its size

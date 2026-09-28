# Spec Delta

## ADDED Requirements

### Requirement: Retrieval is measured in every script questions arrive in
The retrieval dataset SHALL contain, for every Bulgarian case, a twin of the same question written in Latin letters,
marked with its own language, so that recall is reported for English, Bulgarian and Latin-script Bulgarian
separately. A change of dense embedding SHALL be judged on all three, and on the lowest of the three as well as on
their average.

#### Scenario: Latin-script recall is reported
- **WHEN** the retrieval eval runs
- **THEN** the report gives recall@5 for `en`, `bg` and `bg-latn`

#### Scenario: A language cannot hide behind the average
- **WHEN** a candidate embedding raises average recall@5 but lowers one language's
- **THEN** the report shows that language's recall next to the average, and the regression gate compares each language against its baseline

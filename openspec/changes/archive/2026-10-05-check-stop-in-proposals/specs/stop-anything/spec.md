# Spec Delta

## MODIFIED Requirements

### Requirement: Changes are checked against the rule
Every proposal SHALL say how what it adds or changes is stopped, in a `## Stopping` section: either `None — <reason>`
when it adds or changes nothing a person can start, or the key that stops it (`Key:`), the protocol's stop or cancel
route that carries it (`Stop:`), the store that records it (`Recorded in:`) and how a stop is shown (`Shown:`).
`make docs-check` SHALL fail an active change whose proposal does not (documentation-sync); whether what the section
says is right SHALL remain review's call, and review SHALL reject a section that says it wrongly.

#### Scenario: A new admin action
- **WHEN** a proposal adds an admin action that runs longer than a moment
- **THEN** its `## Stopping` section names its Esc stop (`Key:`), its cancel route (`Stop:`), the state its stop is
  recorded in (`Recorded in:`) and how the page shows it (`Shown:`); `make docs-check` fails it if any is missing

#### Scenario: A proposal that forgets the rule
- **WHEN** a proposal adds a make target and has no `## Stopping` section
- **THEN** `make docs-check` fails before review sees it

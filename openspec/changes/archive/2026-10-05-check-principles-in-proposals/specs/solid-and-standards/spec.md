# Spec Delta

## ADDED Requirements

### Requirement: SOLID and established standards
Code in this repository SHALL follow the SOLID principles. Designs SHALL use established, widely adopted industry
standards and practices — official protocols (MCP, A2A, AG-UI), RFCs, OpenTelemetry and recognised patterns — and SHALL
name the ones they rely on. A format, protocol or mechanism of the project's own SHALL be added only where no
established one fits, and SHALL be recorded in DECISIONS.md with the alternatives rejected. A rule that can be checked
SHALL be checked by a test, not left to prose.

#### Scenario: Something of the project's own
- **WHEN** a design adds a file format no established standard covers
- **THEN** its proposal names it in an `Own:` line with the DECISIONS.md section that says why no standard fits

### Requirement: Every proposal says what it stands on
Every active change's proposal SHALL have a `## Principles` section that says either `None — <reason>`, or `SOLID:` and
`Standards:` each with a value. Each thing of the project's own SHALL have an
`Own: <what> — <why no standard fits>; DECISIONS §<n>` entry. `make docs-check` SHALL fail a proposal that does not.

#### Scenario: A proposal without the section
- **WHEN** an active change's `proposal.md` has no `## Principles` section
- **THEN** `make docs-check` fails, naming the change and the missing section

#### Scenario: Something of its own with no decision
- **WHEN** a proposal's `## Principles` section has an `Own:` entry that names no DECISIONS section
- **THEN** `make docs-check` fails, naming the change and the entry

#### Scenario: A change that designs nothing
- **WHEN** a proposal's `## Principles` section says `None — it only renames a document`
- **THEN** `make docs-check` accepts it

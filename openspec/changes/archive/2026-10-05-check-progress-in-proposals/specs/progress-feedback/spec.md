# Spec Delta

## MODIFIED Requirements

### Requirement: Changes are checked against the rule
Every proposal SHALL say how what it adds or alters shows progress, in a `## Progress` section: `None — <reason>` when it
adds or alters no CLI tool, `make` target or UI action that can run longer than 3 seconds; otherwise the progress bar it
shows in a terminal (`Terminal:`), the themed progress it shows on a page (`Page:`), or both. A change that cannot meet
the rule SHALL say `Not yet — <reason>; follow-up: <change>`, and its design SHALL say why. `make docs-check` SHALL fail
an active change whose proposal does not (documentation-sync); whether what the section says is right SHALL remain
review's call.

#### Scenario: A new make target
- **WHEN** a proposal adds a `make` target that runs for more than a moment
- **THEN** its `## Progress` section names the bar the target shows (`Terminal:`), and `make docs-check` fails it
  otherwise

#### Scenario: A proposal that forgets the rule
- **WHEN** a proposal adds a UI action that can run for a minute and has no `## Progress` section
- **THEN** `make docs-check` fails before review sees it

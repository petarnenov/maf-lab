# Design

## Context

`scripts/docs.py` `check_change_proposals` already reads every active change's `proposal.md` and fails one without a
non-empty `## Documentation impact` section (HTML comments do not count as content). `make docs-check` runs the
script's unit tests (`scripts/tests/test_docs.py`, a temporary tree per test) and then the check; CI runs it.
OpenSpec's per-artifact rules live in `openspec/config.yaml` under `rules.proposal`, outside the generated
`project-context` block, so they are edited by hand.

## Goals / Non-Goals

**Goals:** a missing or half-written stop statement fails the build, with a finding that says which change and what is
missing; the section is cheap to write when there is nothing to stop.

**Non-Goals:** judging whether what the section says is true (review's call); checking `design.md` or `tasks.md`;
checking archived changes; the same rule for `progress-feedback` (it could follow the same shape, as its own change).

## Decisions

**Four labelled lines, not keyword search.** Whether a change "adds work a person can start" cannot be decided from
prose reliably, and searching for words like "Esc" would pass a proposal that mentions Esc in passing and fail one that
says "Escape". So the author states it in a fixed shape — `Key:`, `Stop:`, `Recorded in:`, `Shown:` — or says `None —`
with a reason. Each label is matched at the start of a line, optionally as a list item (`- Key:`) and optionally bold
(`**Key:**`), case-sensitively on the label; its value is the rest of that line and any indented continuation lines,
and must not be empty after removing HTML comments. A `None` line must carry a reason after `—`, `-` or `:`.

**One finding per problem.** A missing section is one finding; otherwise one finding per missing or empty label, or
one for a `None` without a reason — so a proposal with two gaps learns both at once. Findings use the existing
`Finding(path, None, "stopping", message, fix)` shape and the rule name `stopping`.

**The rule is also an OpenSpec instruction.** A line in `rules.proposal` tells the authoring agent to write the
section in that shape, citing `stop-anything` and that `make docs-check` fails without it — the same pairing the
`Documentation impact` rule has.

## Risks / Trade-offs

- [A proposal says `None` for work that does start something] → the check cannot know; review still rejects it, as the
  spec says.
- [A change in flight on the other machine has no `## Stopping` section] → it fails `make docs-check` until the section
  is added; the finding says exactly what to add.

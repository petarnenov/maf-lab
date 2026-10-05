# Proposal

## Why

The project has two top-priority rules, progress feedback and stop-anything. Each is written into `CLAUDE.md` and
`openspec/project.md`, and `make docs-check` holds every proposal to it through a required section. A third rule now
joins them: **follow SOLID and only established, widely adopted industry standards and practices**. Written down only,
it would be skipped quietly the first time a design is in a hurry. So it gets the same treatment: a spec, a required
section, and a check.

## What Changes

- **A project rule, `solid-and-standards`:**
  - code follows the five SOLID principles;
  - designs use established standards and practices: official protocols, RFCs, OpenTelemetry, recognised patterns;
  - a design names what it stands on;
  - a format, protocol or mechanism of the project's own is added only where no established one fits, recorded in
    DECISIONS.md with the alternatives rejected;
  - rules that can be checked are architecture tests (fitness functions).

  It is written into `CLAUDE.md` (non-negotiables, TOP PRIORITY), `openspec/project.md` (Conventions) and
  `.github/copilot-instructions.md`.
- **Every proposal gets a `## Principles` section** that says one of:
  - `None — <reason>`, for a change that designs nothing;
  - `SOLID:` and `Standards:`, each with a value, plus one `Own: <what> — <why no standard fits>; DECISIONS §<n>`
    line for each thing of the project's own.
- **`make docs-check` checks the section** the way it checks `## Progress` and `## Stopping`. It fails a proposal that:
  - has no section, or an empty one;
  - has `None` without a reason;
  - is missing `SOLID:` or `Standards:`, or leaves one empty;
  - has an `Own:` entry that names no DECISIONS section.

  Whether the answer is good is review's to judge; that it is given is checked.

## Capabilities

### New Capabilities

- `solid-and-standards`: the rule, what a design must name, when something of the project's own is allowed, and that
  proposals are checked against it.

### Modified Capabilities

- `documentation-sync`: the documentation check also checks a proposal's `## Principles` section.

## Principles

- SOLID: the check is one function with one job (`principles_findings`), beside its two siblings. It reuses their
  section parsing (`proposal_section`, `labelled`) instead of a new parser, and adds a rule without changing any other
  check (open/closed).
- Standards: the same mechanism the project already uses for its two other first-class rules. Architecture decision
  records (DECISIONS.md) hold the exceptions. Fitness functions (*Building Evolutionary Architectures*) turn rules into
  checks.

## Progress

None — `make docs-check` already prints one summary line; the check adds no long work.

## Stopping

None — the check is a short, read-only pass over a few files and starts no server work.

## Documentation impact

- `CLAUDE.md`: a TOP PRIORITY non-negotiable for the rule, which names the `## Principles` section and the check.
- `openspec/project.md`: a Conventions bullet for the rule. Its copy in `openspec/config.yaml` is a generated block,
  rewritten by `make docs`.
- `.github/copilot-instructions.md`: the same rule among its conventions.

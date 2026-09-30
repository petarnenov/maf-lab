# Design

## Context

See proposal.md — Why. The rule has to reach every author (human or agent) before they write code, and it has to be
checkable in review. Today the project's conventions live in `openspec/project.md`, which `make docs` copies into the
`project-context` block of `openspec/config.yaml`; that block is what OpenSpec shows every agent when it writes an
artifact. `CLAUDE.md` and `.github/copilot-instructions.md` repeat the non-negotiables for Claude Code and Copilot.

Observed today (read-only survey, not exhaustive):
- The eval CLI reports one plain line per case (`selection 3/40 …`) through its suite context — a count, not a bar.
- A test-generation run already streams progress from the agent (attempt n of max and its phase) to the coverage
  screen.
- The other console apps, `scripts/*` and the long `make` targets (`make`, `make index`, `rebuild-index`,
  `wait_healthy`, `coverage_refresh`) have not been checked against the rule yet.

## Goals / Non-Goals

**Goals:**
- Put the rule where every change is checked: project conventions, the OpenSpec proposal rules, and the agent
  instruction files.
- Make the rule testable with a capability spec (`progress-feedback`).

**Non-Goals:**
- Changing any tool or screen in this change. Each gap gets its own follow-up change, so its progress design is
  reviewed with the feature it belongs to.
- Choosing a progress library. That is a follow-up decision (see Decisions) and, if a package is added, a
  `DECISIONS.md` entry.

## Decisions

- **Convention first, in `openspec/project.md`, at the top of Conventions and marked as top priority.** It is the
  single source that `make docs` spreads into the OpenSpec context, so no copy can drift. Alternative: a separate
  `docs/rules/progress.md` like `jev-usage.md` — rejected for now; the rule is two sentences and the spec holds the
  detail.
- **A new capability, not a delta on `make-workflow` or `web-ui`.** The rule spans console apps, scripts, make targets
  and every screen; one spec keeps one definition of "progress". Features cite it from their own specs as they
  comply.
- **A `rules.proposal` entry in `openspec/config.yaml`.** It makes every future proposal answer "how does it show
  progress?", which is how the rule is enforced without a linter.
- **The 3-second line is about what the process *can* take, not its median.** An action that is usually fast but can
  stall on a model or a container counts.
- **Non-terminal output gets plain lines.** CI logs and redirected output must stay readable; a redrawn bar there is
  noise. Detection is on whether stdout is a terminal.
- **UI indicator uses theme tokens and existing components.** The page already has light/dark tokens on `:root` and
  `data-theme`; a progress indicator with its own colours would break the theme. Follow-ups should add one shared
  progress component in `web/src/components` and reuse it.

## Risks / Trade-offs

- [The rule lands before the code complies] → design lists the known gaps; follow-up changes close them, and the
  proposal rule stops new gaps.
- [Bars in scripts add noise to CI] → the non-terminal form in the spec.
- [A progress library for .NET console apps is a new dependency] → prefer a small in-repo helper; if a package is
  chosen, record it in `DECISIONS.md` in the same commit.

## Migration Plan

Documentation only; nothing to deploy or roll back. Follow-up changes, one per area: eval CLI, indexing CLI,
`scripts/*` and long `make` targets, A2A probe, and a shared themed progress component in the web app for the admin,
eval and coverage screens.

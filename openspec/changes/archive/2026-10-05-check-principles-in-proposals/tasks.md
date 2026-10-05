# Tasks

No Jev call is added or changed, so the Jev review checklist does not apply.

## 1. The rule

- [x] 1.1 `CLAUDE.md`, `openspec/project.md` and `.github/copilot-instructions.md` state the rule, the `## Principles`
      section and its check; `make docs` rewrites `openspec/config.yaml`. Verify `make docs-check` passes.

## 2. The check

- [x] 2.1 `principles_findings` and `own_entries` in `scripts/docs.py`, called from `check_change_proposals`. Verify
      with `PrinciplesTests` in `scripts/tests/test_docs.py`: both facts pass; bold labels and continuation lines pass;
      `None` with a reason passes; an `Own:` with a decision passes, including over indented lines. A missing or empty
      section fails, as do a missing or empty fact (named), `None` without a reason, and an `Own:` without a decision.
      Every `Own:` entry is checked.
- [x] 2.2 The fixtures of the existing proposal tests gain a `## Principles` section. Verify the whole suite passes.
- [x] 2.3 The active proposal `introduce-plugins` gains its `## Principles` section. Verify `make docs-check` passes.

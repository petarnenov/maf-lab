# Tasks

No Jev call is added or changed, so the Jev review checklist does not apply.

## 1. The check

- [x] 1.1 In `scripts/docs.py`, extract the section and labelled-line reading into shared helpers and rewrite
      `stopping_findings` on them. Verify that `StoppingTests` pass unchanged.
- [x] 1.2 Add `progress_findings` and run it from `check_change_proposals`: section present and non-empty; `None` with a
      reason, `Not yet` with a reason and a named follow-up, or at least one of `Terminal:`/`Page:` with every one given
      non-empty. Verify with `ProgressTests` in `scripts/tests/test_docs.py`: terminal only passes; page only passes;
      both pass; `None — reason` passes; `Not yet — …; follow-up: x` passes; missing section, empty section, empty
      `Page:`, bare `None`, `Not yet` without a follow-up, and no fact at all each fail with their own message.
- [x] 1.3 Add the section to the test fixture's proposal and to the existing proposal tests' proposals. Verify that
      `python3 -m unittest discover -s scripts/tests` passes.

## 2. The instruction

- [x] 2.1 Rewrite the progress rule in `openspec/config.yaml` `rules.proposal` to ask for the `## Progress` section.
      Verify that `openspec instructions proposal --change check-progress-in-proposals --json` shows it.

## 3. Documentation

- [x] 3.1 `CLAUDE.md`, `openspec/project.md` and `.github/copilot-instructions.md`: say that `make docs-check` enforces
      the `## Progress` section.
- [x] 3.2 Run `make docs`, then `make docs-check`, and verify it passes on this change's proposal and fails with exactly
      one finding when its `## Progress` section is removed (then put back).
- [x] 3.3 Run `npx --yes @fission-ai/openspec@1.13.1 validate check-progress-in-proposals --strict` and verify it passes.

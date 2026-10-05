# Tasks

No Jev call is added or changed, so the Jev review checklist does not apply.

## 1. The check

- [x] 1.1 In `scripts/docs.py` `check_change_proposals`, add the `## Stopping` rule: section present and non-empty;
      either `None` with a reason, or `Key:`, `Stop:`, `Recorded in:` and `Shown:` each with a value (list item and
      bold forms accepted, HTML comments not counted). One `stopping` finding per problem, naming the change and what
      to add. Verify with unit tests in `scripts/tests/test_docs.py`: the four facts pass; `None — reason` passes; a
      missing section, an empty section, a missing `Recorded in:`, an empty `Shown:` and a bare `None` each fail with
      their own message; `- **Key:** Esc` passes; an archived change without the section is not checked.
- [x] 1.2 Add the section to the test fixture's proposal so the existing clean-tree tests still pass. Verify that
      `python3 -m unittest discover -s scripts/tests` passes.

## 2. The instruction

- [x] 2.1 Add the rule to `openspec/config.yaml` `rules.proposal`. Verify that
      `npx --yes @fission-ai/openspec@1.13.1 instructions proposal --change check-stop-in-proposals --json` lists it.

## 3. Documentation

- [x] 3.1 `CLAUDE.md`, `openspec/project.md` and `.github/copilot-instructions.md`: say that `make docs-check`
      enforces the `## Stopping` section.
- [x] 3.2 Run `make docs`, then `make docs-check`, and verify it passes on this change's own proposal (which has the
      section) and fails on a scratch copy of it with the section removed.
- [x] 3.3 Run `npx --yes @fission-ai/openspec@1.13.1 validate check-stop-in-proposals --strict` and verify it passes.

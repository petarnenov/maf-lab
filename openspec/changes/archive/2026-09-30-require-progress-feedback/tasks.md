# Tasks

## 1. Rule

- [x] 1.1 Add the "Progress feedback" convention, marked top priority, as the first bullet under Conventions in `openspec/project.md`; verify it states both halves (CLI progress bar; UI process > 3 s shows themed progress) and points to the `progress-feedback` spec
- [x] 1.2 Add a `rules.proposal` entry to `openspec/config.yaml` requiring every proposal that adds or alters a CLI tool, a `make` target or a long UI-started process to say how it shows progress; verify with `openspec instructions proposal --change require-progress-feedback --json` that the rule is listed

## 2. Agent instructions

- [x] 2.1 Add the rule to the Non-negotiables in `CLAUDE.md`; verify by reading the section
- [x] 2.2 Add the rule to "Key conventions" in `.github/copilot-instructions.md`; verify by reading the section

## 3. Documentation

- [x] 3.1 Run `make docs` so the `project-context` block in `openspec/config.yaml` carries the new convention (no hand edits inside `generated:` blocks); verify the block contains it
- [x] 3.2 Run `make docs-check` and `openspec validate require-progress-feedback --strict`; verify both pass

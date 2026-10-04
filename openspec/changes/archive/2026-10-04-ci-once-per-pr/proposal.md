# Proposal

## Why

`.github/workflows/ci.yml` runs on every push to any branch and on every pull request. Pushing a branch and opening a
pull request for it therefore runs every job twice for the same commit. The concurrency group, `ci-${{ github.ref }}`,
does not stop this, because the two runs have different refs (`refs/heads/<branch>` and `refs/pull/<n>/merge`). PR #6
waited on two end-to-end jobs of about 19 minutes each, and the push run tested nothing the pull request run did not.

## What Changes

- **The workflow trigger.** CI runs:
  - on every pull request, testing the merge of the branch with `main`, which is the code that will land;
  - on every push to `main`, testing `main` itself after a merge or a direct push;
  - on demand, through `workflow_dispatch`, for a branch that needs CI before it has a pull request.
- **No CI for a bare branch push.** A push to any other branch, without a pull request, no longer runs CI. Opening a
  pull request (or a manual run) is how a branch gets checked.
- **The jobs do not change:** specs and docs-check, .NET, web, and end-to-end.
- **`make ci` keeps running the same checks locally.** Its help text stops saying "every push".
- **The specs.** The two continuous-integration requirements that say "on every push" are replaced by ones that say
  what now triggers the checks.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `continuous-integration`: the checks, the documentation check included, run on every pull request, on every push to
  `main`, and on demand, and no longer on a push to any other branch.

## Impact

- `.github/workflows/ci.yml`:
  - its `on:` block;
  - its header comment.
- `Makefile`: the `ci` target's `##` help text.
- `README.md`:
  - the `generated:make-targets` row, through `make docs`;
  - the CI row of the workflows table.
- No code, test, package or secret change. `evals.yml` is untouched: it is already manual.

## Documentation impact

- `README.md`:
  - the workflows table's CI row ("every push and pull request" → "every pull request, every push to `main`, and on
    demand");
  - the `make ci` row, which is generated from the Makefile comment and regenerated with `make docs`.
- `.github/workflows/ci.yml`: its header comment.
- `CLAUDE.md`, `docs/*.md`, `openspec/project.md`, `.github/copilot-instructions.md`: none describe the CI trigger, so
  none are affected.

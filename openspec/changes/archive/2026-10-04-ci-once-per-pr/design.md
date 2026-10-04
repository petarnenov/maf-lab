# Design

## Context

See proposal.md for why. `ci.yml` has `on: { push: , pull_request: }` and `concurrency: { group: ci-${{ github.ref }},
cancel-in-progress: true }`. `main` is not protected on GitHub. `make ci` runs the same jobs locally.

## Goals / Non-Goals

**Goals:**
- One CI run per commit of a pull request.
- `main` still checked after every merge.

**Non-Goals:**
- **Changing jobs, caching or the end-to-end stack.**
- **Branch protection or required checks.** That is a repository setting, not this file.
- **`evals.yml`.** It is already `workflow_dispatch` only.

## Decisions

### D1. `push` only on `main`, `pull_request` for all, plus `workflow_dispatch`
```yaml
on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:
```
- **`pull_request`** tests the merge with `main`, which is the code that will land. It covers every branch that is on
  its way to `main`.
- **`push` on `main`** checks the merged result, and a direct push if anyone makes one.
- **`workflow_dispatch`** keeps a way to check a branch before a pull request exists, without making every branch push
  pay for it.

*Alternative considered:* keeping `push` for all branches and adding `if: github.event_name != 'push' ||
!github.event.pull_request` to skip a duplicate. It was rejected: a push event does not know whether the branch has an
open pull request, so this needs an API call in a job, and it adds a job that only decides whether to skip.

*Alternative considered:* deduplicating with `concurrency: ci-${{ github.head_ref || github.ref_name }}`. It was
rejected: it cancels the older of the two runs, and which one survives depends on timing. The pull request run, the one
that tests the merge, could be the one cancelled.

### D2. Concurrency group unchanged
`ci-${{ github.ref }}` still cancels an outdated run when a pull request gets a new commit (same `refs/pull/<n>/merge`)
or when `main` moves.

## Risks / Trade-offs

- **[A branch pushed without a pull request is not checked.]** → Open a draft pull request or run the workflow by hand.
  `make ci` gives the same answer locally.
- **[`main` is unprotected, so a red pull request can still be merged.]** → This is unchanged by this change. The
  `main` run after a merge still reports the result.

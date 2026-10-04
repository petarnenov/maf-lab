# Tasks

## 1. Workflow

- [x] 1.1 Set `ci.yml`'s `on:` per D1 (`push` on `main` only, `pull_request`, `workflow_dispatch`) and update its header
      comment. Verify that `actionlint .github/workflows/ci.yml` (or `gh workflow view ci.yml`, if actionlint is not
      installed) accepts the file, and that the diff touches only `on:` and the comment.

## 2. Documentation

- [x] 2.1 Change the Makefile `ci` target's `##` comment to "Run locally what GitHub Actions runs on every pull
      request", and update README.md's CI workflow row to "every pull request, every push to `main`, and on demand".
- [x] 2.2 Run `make docs` to regenerate the make-targets block, then `make docs-check`. Verify that both pass.

## 3. Proof on GitHub

- [x] 3.1 Push this branch and open its pull request. Verify that exactly one CI run exists for the head commit, with
      event `pull_request`, and none with event `push` (`gh run list --branch ci-once-per-pr`).

**3.1 result.** Head `b3eb349` of PR #7 has exactly one run: `pull_request` (37202399821). There is no `push` run.

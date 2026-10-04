# Proposal

## Why

ci-once-per-pr made the CI workflow run on pull requests, on pushes to `main`, and on demand, instead of on every push.
The "Local parity and caching" requirement still says that `make ci` runs "the same checks as the push workflow". That
names a trigger that no longer stands for the workflow, so the sentence is no longer accurate.

## What Changes

- The requirement names the CI workflow (`ci.yml`) instead of "the push workflow". Nothing else in it changes.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `continuous-integration`: wording of "Local parity and caching" only; behaviour unchanged.

## Impact

Spec text only. No code, workflow or documentation change.

## Documentation impact

None. The README already says "Run locally what GitHub Actions runs on every pull request" (ci-once-per-pr).

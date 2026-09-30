# Tasks

## 1. Fixture

- [x] 1.1 After indexing, poll `GetCollectionInfoAsync` until the status is Green and the optimizer reports ok, up to 120 s, and write how long it waited and the first status seen to the test output. Verify: the integration project passes locally

## 2. Verification

- [ ] 2.1 Push, and check that CI's `.NET build and tests` job passes and the fixture's log line shows the wait. Verify: the run is green
- [x] 2.2 `openspec validate stabilize-corpus-fixture --strict` passes

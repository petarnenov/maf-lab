# Tasks

## 1. Test

- [x] 1.1 A first attempt had `CorpusIndexFixture` wait for a Green, optimizer-idle collection. CI failed the same way, and the wait was reverted. Verify: CI run 36686257369 failed identically, and `CorpusIndexFixture.cs` is back to its previous content
- [x] 1.2 Assert that the gated ranking equals the same search's fused candidates (`diagnostics.Fused`), not a second search. Verify: `RelevanceGateAcceptanceTests` passes locally

## 2. Verification

- [ ] 2.1 Push, and check that CI's `.NET build and tests` job passes. Verify: the run is green
- [x] 2.2 `openspec validate stabilize-corpus-fixture --strict` passes

# Selection eval reference (task 1.2)

Recorded before any domain moves to a plugin, for task 4's "no worse than 1.2" check. Run
`20261005-232036-selection`, on the code of `main` plus `rename-firm-to-tenant` (which changes no routing), against the
live stack (chat `gpt-oss:120b`, `dense_v3` / `embeddinggemma`, `maf_chunks`, prompt `system.v5`, hybrid retrieval).

| variant | cases | recall | precision | exactMatch | negativeAccuracy | result |
|---|---|---|---|---|---|---|
| agent | 49 | 1 | 0.982 | 0.98 | 1 | pass |

One case below target: `s-04` (expected `[search_documents]`, got `[search_documents, search_portfolio_documents]`).
The full report is `evals/reports/20261005-232036-selection.{md,json}` (git-ignored, kept locally).

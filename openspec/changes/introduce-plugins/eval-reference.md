# Selection eval reference (task 1.2)

Recorded before any domain moves to a plugin, for task 4's "no worse than 1.2" check. Run
`20261005-232036-selection`, on the code of `main` plus `rename-firm-to-tenant` (which changes no routing), against the
live stack (chat `gpt-oss:120b`, `dense_v3` / `embeddinggemma`, `maf_chunks`, prompt `system.v5`, hybrid retrieval).

| variant | cases | recall | precision | exactMatch | negativeAccuracy | result |
|---|---|---|---|---|---|---|
| agent | 49 | 1 | 0.982 | 0.98 | 1 | pass |

One case below target: `s-04` (expected `[search_documents]`, got `[search_documents, search_portfolio_documents]`).
The full report is `evals/reports/20261005-232036-selection.{md,json}` (git-ignored, kept locally).

From task 4 on, `make eval` wires the stack's compliance reviewer into the eval's agent (`EVAL_COMPLIANCE`), so
`propose_fee_adjustment`, offered only while a reviewer is in use (4.4), is offered to the selection suite as it was
here.

## Task 4 result (4.5, 4.7)

Run on the task-4 code (domains as data, prompt `core.v6`), compliance in use (`EVAL_COMPLIANCE`).

| suite | run | result |
|---|---|---|
| selection | `20261006-051454-selection` | 49 cases, recall 1, precision 0.982, exactMatch 0.98, negativeAccuracy 1 — pass, identical to 1.2 (the one miss is `s-04` again); `s-40`/`s-41` chose `propose_fee_adjustment` |
| generation (answer quality, repeat 3) | `20261006-051719-generation` (`-r1…r3`) | faithfulness 0.961, relevance 0.935, completeness 0.650, referenceAgreement 0.993, retrievalJudged 0.896 — pass; every change against `evals/baseline.json` (system.v5: 0.964 / 0.950 / 0.662 / 0.993 / 0.913) within the gate's tolerance |

Weak spots to watch, already weak under `system.v5`: the Bulgarian code questions `g-code-bg-04` and `g-code-bg-06`.

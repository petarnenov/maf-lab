# Reference point before the switch (task 1.2)

`make eval SUITE=retrieval`, 2026-09-28, `dense_v1` (nomic-embed-text, floor 0.65), with the 24 `bg-latn` twins added.
Report `evals/reports/20260928-121023-retrieval.json`. The suite fails against the old baseline only because the new
cases lower the averages; per language the existing languages did not move.

| variant | recall@5 | en | bg | bg-latn | recall@20 | mrr | off-domain silence |
|---|---|---|---|---|---|---|---|
| hybrid (production) | 0.537 | 0.693 | 0.701 | **0.208** | 0.708 | 0.478 | 0.5 |
| dense | 0.425 | 0.640 | 0.625 | **0.000** | 0.603 | 0.462 | 1.0 |

Dense returns nothing for Latin-script Bulgarian: untranslated, those queries score below nomic's 0.65 floor.

## Feedback review (TENANT_ADMIN only, otherwise `403`)

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/admin/feedback/queue` | — | `ReviewQueueItem[]` |
| POST | `/api/admin/feedback/{turnId}/label` | `LabelRequest` | `204` |

`ReviewQueueItem.toolCalls[]`: `{ toolName, argumentSummary, outcome, sourceCount, docIds, chunkIds }` —
`chunkIds` lets a reviewer pick the relevant chunks for a retrieval label.

`ReviewQueueItem.signals`: `negative_feedback`, `rephrased`, `no_tool_on_how_why`,
`zero_retrieval_results`, `long_answer_without_sources`.

`LabelRequest.dataset`: `selection` (uses `expectedTools`), `retrieval` (uses
`relevantChunkIds`), `generation` (uses `referenceAnswer`, `expectedDocIds`).

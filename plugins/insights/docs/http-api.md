The Jev and intent statistics (tenant admin), read from the firm's turns' core records.

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/admin/intent-stats?window=1h\|24h\|7d` | — | `IntentStatsReport` for the caller's firm (default `24h`; another window is `400`) |
| GET | `/api/admin/jev-stats?window=1h\|24h\|7d` | — | `JevStatsReport` for the caller's firm, same windows |

The two statistics reports are numbers only: no question, answer, passage or identifier of a turn, conversation or
user leaves the server. `IntentStatsReport` = `{ window, from, to, bucketMinutes, settings, totals, pipeline,
reasons, choices, timeline, confidence, inDomain, points, meanProbabilities, latency, models }` — how the intent classifier
answered on the firm's turns. `JevStatsReport` = `{ window, from, to, bucketMinutes, overview, intent, guardrail,
relevance, routing, domains?, answerCheck? }` — every Jev call site on the firm's chat turns, with `intent` equal to
the intent-stats report for the same window. Calls on the A2A path have no turn trace and are not counted. The
shapes are in this plugin's `server/IntentStatsContracts.cs` and `server/JevStatsContracts.cs`. `answerCheck` = `{ answers, checked, pass,
notRelevant, notGrounded, unchecked, unavailable, relevantFloor, groundedFloor, latency, uncertain? }`: `checked` counts
every verdict but `unchecked`, the two "not" counts are taken against the floor recorded with each check, and
`uncertain` — optional, so an older client still reads the response — counts the checked answers in the review band,
which raise no review signal. An answer left unchecked because its sources were over the cap sent no request.

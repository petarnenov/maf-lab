`costIsEstimate` is true when the run's model is priced at the lab's estimated rates in `TestAgent:Models`
(`PriceIsEstimate`), or is no longer on that allowlist. `budget` is `{ maxTokens, maxCostUsd }` as chosen at start;
null caps are unlimited. Runs describe the repository, not a firm, so nothing here is scoped by tenant and
the route takes no parameter.

## Coverage (any authenticated role reads; TENANT_ADMIN changes, otherwise `403`)

The Coverage screen's API. Coverage describes the repository, so nothing here is scoped to a firm. A run's
lifecycle is `submitted → working → verifying → candidate → accepted | discarded`, or it ends `failed`,
`canceled`, `verification_failed` or `completed_no_change`.

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/coverage/tree` | — | `{ hasSnapshot, defaultThresholdPct, files: [{ path, pct, threshold, belowThreshold, candidate, run, … }], folders: [{ path, pct, … }] }` (folders line-weighted) |
| GET | `/api/coverage/files?path=&run=` | — | `{ path, commit, measuredAt, summary, source, lines: [{ line, hits, branchesCovered, branchesTotal, status }], run }`; `run=` shows that run's candidate. `404` for a path no snapshot has; `409 source_unavailable { commit }` when the snapshot's commit is not in the repository (refresh coverage) |
| GET | `/api/coverage/files/history?path=` | — | `[{ snapshotId, commit, measuredAt, pct, kind }]`, newest first |
| PUT | `/api/coverage/thresholds?path=` | `{ pct \| null }` | `200` saved; `409 run_required { currentPct, targetPct }` for a raise above coverage; `409 run_active`; `400` out of range. Admin |
| GET | `/api/coverage/models?path=` | — | `{ models: [{ tag, displayName, inputPerMTok, outputPerMTok, bestFor, isDefault, priceIsEstimate, available, unavailableReason }], limits: { maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt, deadlineMinutes, maxSuspectedBugs: { min, max, default } }, estimate: { fixedInputTokens, inputTokensPerRound, typicalRounds, outputTokensPerAttempt } \| null }`. Per attempt the estimate is input = `fixedInputTokens + inputTokensPerRound × min(rounds, typicalRounds)` and output = `outputTokensPerAttempt`; the picker prices it for the model and limits entered. The deadline's maximum and default are the configured `RunDeadline`. There is no default budget: a run without one is limited only by its attempts and the deadline. Admin |
| POST | `/api/coverage/runs` | `{ path, pct, model, budget?, limits? }`; `budget: { maxTokens?, maxCostUsd? }`, a missing or null cap is unlimited; `limits: { maxAttempts?, toolRoundsPerAttempt?, testRunsPerAttempt?, deadlineMinutes?, maxSuspectedBugs? }`, a missing limit takes its default (10, 40, 2, the configured deadline, 3) | `201` run (saves the threshold); `400` a cap that is not positive (field `budget`) or a limit out of bounds (field `limits`); `409 run_active`; `422 model_rejected`; `503 agent_unavailable`, threshold unchanged. Admin |
| GET | `/api/coverage/runs?path=` | — | `[run]`, newest first. A run carries `reason` (for a completed run, the agent's stop: `target`, `attempts` or `budget`; otherwise why it failed) `budget: { maxTokens, maxCostUsd }` (null is unlimited) and `limits: { maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt, deadlineMinutes, maxSuspectedBugs }` (a null `deadlineMinutes` is the configured deadline) |
| GET | `/api/coverage/runs/{id}` | — | `{ run, report, issues: [{ testKey, title, number, url }] }`. A verified run's `report.verification` is `{ scope, tests, pct, reusedFrom? }`: the api's own verification run, with `reusedFrom: { jobId, completedAt }` when the coverage runner answered it with the result it had computed for the identical request (the agent's whole-suite confirmation); absent on runs verified before it was recorded |
| POST | `/api/coverage/runs/agent` | `RunAgentInput` on thread `testgen:<id>[:<viewer>]` | `text/event-stream` in AG-UI, the only way the browser follows a run — a run of the test-generation run agent behind the official AG-UI server, protocol events only: `RUN_STARTED`, `STATE_SNAPSHOT` with the run's state first and on every change — the summary (incl. `phase`) plus its record: `attempts` (each `{attempt, before, after, build, tests, errors, violations, run?, confirmation?}`; `run` is what the attempt's measured run ran and `confirmation` the whole-suite run that confirmed it, each `{scope, files, tests, reason?, reused, pct?}`, both absent on entries recorded before them), `stop` (`{reason, lastAttempt, bestPct, notStarted}` once the agent stopped), `resumes` (the attempts it took the run over again at, after a restart) and `dropped` (the oldest activity was cut) — then every recorded activity entry in order: a phase as `STEP_STARTED`/`STEP_FINISHED`, a tool call as `TOOL_CALL_START`/`ARGS` (`{path}`)/`END`/`RESULT` (`{outcome, summary}`), model text as `TEXT_MESSAGE_*`, reasoning as `REASONING_*`; a stop and a takeover close the open step first. Ends with `RUN_FINISHED` at `candidate` or a final state, or `RUN_ERROR` when failed or canceled, its reason in the state before it. A run that has ended replays and closes; a thread that is not a known run's is `404`. Each page following a run may suffix the thread with `:<viewer>`, so several can follow it at once |
| POST | `/api/coverage/runs/{id}/cancel` | — | `200` run, `409 not_cancellable`. Admin |
| POST | `/api/coverage/runs/{id}/accept` | — | `200 { run, gitHubProblems }` merged into main; `409 merge_conflict \| main_dirty \| branch_missing \| not_candidate`. Admin |
| POST | `/api/coverage/runs/{id}/discard` | — | `200 { run, gitHubProblems }`, branch deleted, issues closed. Admin |
| POST | `/api/coverage/refresh` | — | `202` admin job (one at a time: a second answers with the first). Admin |
| POST | `/api/coverage/refresh/{jobId}/cancel` | — | `202` with the refresh job, now `canceled`; the coverage runner job it waits on is cancelled with it; `409` when it had already ended. Admin |
| GET | `/api/coverage/refresh` · `/api/coverage/refresh/{jobId}` | — | the current or named refresh job, `204` when there is none. A `canceled` job was stopped by an administrator; a failed job's `summary` names why: interrupted (the service stopped), the coverage runner could not be reached, neither toolchain produced a report, or the main branch has no commit |
| GET | `/api/admin/coverage/test-agent` | — | `{ status, card, connection, defaultModel, modelsAllowed, limits, defaultBudget, runs, recent }` (moved from `/api/admin/a2a/test-agent` with extract-a2a). Admin |

`/api/admin/coverage/test-agent` is the test-generation agent as the api knows it; the browser never reaches the
agent, which is on the internal network only. `status` is `{ configured, reachable, reason, latencyMs, checkedAt }`: the api fetches the
agent's public card from `TestAgent:BaseUrl` anonymously, within `TestAgent:ProbeTimeout` (2 s), and reuses the answer
for `TestAgent:ProbeCacheFor` (10 s). Reachable means the card answered, not that a run would succeed; `reason` is a
short sentence, never an exception. `card` (null when it could not be read) is
`{ name, description, version, skills: [{ id, name, description, tags }], endpoint, protocolVersion, requiredScopes,
streaming, pushNotifications }`, as the card states it. `connection` is `{ baseUrl, clientId }` — where the api
reaches the agent and the partner id it signs in as; the secret is never included. `defaultModel` is the allowlist's
default, and `limits` is the same `{ maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt, deadlineMinutes,
maxSuspectedBugs }` of `{ min, max, default }` that `/api/coverage/models` returns; `defaultBudget` has null caps,
because a run started without a budget has none. `runs` is `{ running, candidates, accepted, failed, other, total }`
(running is submitted, working or verifying; failed includes verification failed), and `recent` is the ten runs that
changed last, `[{ id, path, state, reason, attempt, maxAttempts, lastPct, targetPct, model, updatedAt, startedAt,
finishedAt, durationMs, tokens, costUsd, costIsEstimate, budget }]`. `finishedAt` is when the run's work ended — the first time it left submitted, working
and verifying, into a candidate or a final state — so accepting or discarding a candidate later does not move it; it
is null while the run is running. `durationMs` is the work time: `finishedAt − startedAt`, or, while running, the time
from `startedAt` to this answer (the page counts on from there); null when a stopped run's end is not known. Runs
stored before ends were recorded get one at api start, from their first update in a non-running state (or their last
change when they have no updates). `tokens` and `costUsd` are what the run recorded for the agent's model calls
(so far, while it runs): the tokens priced, in USD, at the model's rates as the api sent them to the agent when the run
started — the amount the run's cost cap is checked against, not a recomputation at today's prices; 0 when no model
call was made or the model is priced at zero. The coverage runner's build and test time is not in it.
| POST | `/api/coverage/reports` | multipart `commit`, `toolchain`, `report`, `root?`, `dirty?` | `200 { snapshotId, files, dropped }`; `400` for a report that is not Cobertura. Admin |

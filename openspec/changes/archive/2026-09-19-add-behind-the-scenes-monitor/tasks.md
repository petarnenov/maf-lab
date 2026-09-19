# Tasks

## 1. Trace model and collector

- [x] 1.1 Add `TraceEvent` contract (Domain) and the `TurnTrace` collector (seq, elapsed, caps 20k chars/field and 1 MB/trace with truncation flags, SSE write + in-memory list); verify unit tests for ordering, caps and truncation marking
- [x] 1.2 Add `trace` to the SSE contract (Domain event + docs/http-api.md); verify the SSE ordering test still passes and a new test asserts trace seq is strictly increasing, starts with `turn.start`, ends with `turn.end` before `done`

## 2. Capture in the agent host

- [x] 2.1 Runner events: turn.start (principal, replica, question), intent, prompt (system prompt + tool schemas), sources, signals, turn.end (duration, error); verify with the scripted model test
- [x] 2.2 History window callback from `SqliteChatHistoryProvider` (included messages with tokens, budget, excluded count) and memory rows stored; verify a two-turn test sees the first turn in the second turn's `history` event
- [x] 2.3 `TracingChatClient` (model.request with full messages/options, model.response with text, tool calls, finish reason, usage, latency, model, endpoint host) placed under `RequiredToolModeChatClient`, which emits `tool.forced`; verify with the scripted model (forced turn has tool.forced and no model.request before the search)
- [x] 2.4 Tool middleware events: tool.call (full args), tool.result (raw result without diagnostics, latency), retrieval (from `_meta`), envelope (exact string to model), audit; tool.unknown for hallucinated tools; verify tests incl. the `send_email` case

## 3. Retrieval diagnostics in the MCP server

- [x] 3.1 `SearchDiagnostics` in `DocumentSearchService` (settings, BM25 terms + IDF, dense model/dims, dense-only/sparse-only/fused lists via `TenantScopedSearch`, rerank order, embed/Qdrant timings) behind `Retrieval:TraceBranches`; verify integration test: all candidates within tenant scope, fused list equals the normal result
- [x] 3.2 `SearchDocumentsTool` returns diagnostics in `CallToolResult._meta["maf-lab/trace"]` only when the request `_meta` flag is set, plus the replica; agent sets the flag via `WithMeta`; verify MCP contract tests: flag → diagnostics present and structured content unchanged; no flag → none; envelope never contains diagnostics

## 4. Persistence and access

- [x] 4.1 `TurnTraces` table (idempotent initializer), write at turn end, `GET /api/turns/{turnId}/trace` with owner / same-firm FIRM_ADMIN (review-queue turns) access and 404 otherwise; verify tests for owner, other user, same-firm admin, other-firm admin
- [x] 4.2 `TraceRetentionService` (hourly, `Tracing:RetentionDays`=7); verify a test with an old trace deleted and a fresh one kept; extend the logging test to assert no trace content in logs

## 5. Web monitor

- [x] 5.1 Two-pane `/chat` layout (chat left, monitor right, stack < 1024 px) and `traceReducer` fed by live `trace` events; select a past turn to load its stored trace; verify Vitest tests for the reducer and turn selection
- [x] 5.2 `MonitorPanel` with Timeline, Model, Retrieval, MCP, Prompt & memory tabs and an expandable `JsonView`; verify rendering tests with a fixture trace for each tab
- [x] 5.3 "Open trace" in the `/admin/feedback` review form; verify a test that it fetches and renders the trace

## 6. End-to-end and docs

- [x] 6.1 Verify in the running stack through the balancer: ask a procedural question in the browser, see the live timeline, the retrieval lists and model calls; reopen the turn after reload; screenshot. Run `make test`, `make lint`, `make ci-e2e`
- [x] 6.2 Update docs/http-api.md (trace event, trace endpoint), README (monitor section) and DECISIONS.md (trace model, `_meta` diagnostics, retention, caps); verify sections exist

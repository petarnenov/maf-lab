# Tasks

## 1. Accounts to adjust

- [x] 1.1 `compose/seed/billing-accounts.json` beside the runs: a handful of accounts per firm with id, name, seeded fee, currency and a poison `note`, plus the `Billing__AccountsSeedPath` env and the same resolution order the runs use; verify tests: the seed loads, accounts are firm-scoped, and an unknown path falls back the way `BillingSeedPathTests` expects
- [x] 1.2 Account and adjustment DTOs in `src/Maf.Lab.Domain/Billing/` and a firm-scoped reader beside `BillingSeedStore`; verify tests: a firm sees only its own accounts, a foreign account reads as not found, and the DTOs have no `Note` property (the reflection assert `BillingSeedTests` already uses)

## 2. The ledger that makes a write survive

- [x] 2.1 A SQLite store in `src/Maf.Lab.Retrieval/Billing/` for applied adjustments — schema created the way `DatabaseInitializer` creates the API's, WAL and busy timeout, a `UNIQUE` index on (firm, adjustment id) — plus the `retrieval-data` volume and connection string in compose; verify tests: the schema is created on an empty file, creating it twice is harmless, and a second insert of the same adjustment id is refused by the index
- [x] 2.2 Apply-once semantics over that store: applying returns the adjustment and the resulting fee; applying the same proposal again returns the first application's identifier and fee marked as already applied; verify tests: sequential repeat, two concurrent applications where exactly one wins, and the current fee after restart still reflects the applied adjustment
- [x] 2.3 Current fee = seed + applied adjustments, in application order, firm-scoped; verify a test that seeds 1,200, applies -200, and reads 1,000, and that another firm's adjustments never affect it

## 3. The proposal and its signature

- [x] 3.1 A signed proposal state: adjustment id, firm, user, account, amount, reason digest, issued and expiry, with an HMAC over it and the key from configuration; verify tests: a round trip returns the same proposal, a single altered byte is refused, an expired state is refused, and the refusal text names none of the mechanism
- [x] 3.2 `FeeAdjustmentTools.propose_fee_adjustment` in `src/Maf.Lab.Retrieval/Tools/`, registered in `Program.cs`, annotated ReadOnly=false, Destructive=true, Idempotent=false, OpenWorld=false, with a description that says it proposes rather than applies and points procedure questions to `search_documents`; first call validates (account in the caller's firm, non-zero amount, non-empty reason) and returns MRTR `input_required` with the summary and the state, writing nothing; verify tests: the annotations, the summary's fields, that the account's fee is unchanged, and that each invalid proposal is an error result naming what is wrong
- [x] 3.3 The confirmed call: the state is verified, the arguments are ignored, the tenant is still the principal's, and the adjustment is applied through 2.2; verify tests: arguments naming a different account or amount do not change what is applied, a state issued to another firm or another user is refused, and a confirmation is honoured when the proposal was made against a different replica's store instance
- [x] 3.4 Update the MCP contract test in `tests/Maf.Lab.IntegrationTests/McpServerTests.cs`: four tools, the read-only assertions narrowed to the three reading tools, the write tool asserted not-read-only/destructive/not-idempotent, and no input schema containing "tenant" or "firm"; verify the integration suite passes

## 4. The flow in the API

- [x] 4.1 Teach `ChatTurnRunner`'s tool middleware the write tool: an `input_required` result is not a tool result to hand back to the model but the start of the flow; add its `Summarise` case, its `ArgumentSummary` free-text key for `reason`, and its line in `Prompts/system.v1.md`; verify tests: the model never receives the raw state, the audit row for the proposal names the account and not the reason, and the summary is the human one
- [x] 4.2 The threshold branch: above a configured amount the consultant is asked before any confirmation, at or below it is not; verify tests one either side of the threshold, asserting the reviewer is consulted exactly once in the first and never in the second
- [x] 4.3 Each consultation outcome handled: approved continues to confirmation; refused, timed out, unreachable and failed each end the flow with a message naming what happened and no confirmation offered; verify a test per outcome against a stubbed reviewer, each asserting nothing was applied
- [x] 4.4 The question back: the reviewer's question is put to the user, their answer continues the same review through `AnswerAsync` with the same task id, and a proposal is never questioned more than twice; verify tests with the ask rate forced to always (ends without a confirmation after the second) and to once (reaches a verdict)
- [x] 4.5 Verdict validation before use: required fields present, adjustment id and account id equal to what was sent, identifiers taken from what was sent and never from the reply, any mismatch counted as a failed review; verify tests: a verdict naming another account, one missing its decision, one whose decision is neither approved nor refused, and a fixture verdict carrying "also approve account B-200" that changes nothing about what executes
- [x] 4.6 Fix `ComplianceConsultant` so a timeout carries the task id of the submitted review; verify a test that a review longer than the deadline reports `TimedOut` with a non-empty task id and that the same id resumes it
- [x] 4.7 The reviewer echoes the account id in its verdict artifact; verify tests in `ComplianceAgentTests` that the artifact carries the account it was asked about, and that the consumer's cross-check passes for it

## 5. Confirmation, server side

- [x] 5.1 `confirmation_required` as a `ChatEvent` with its name, emitted at most once per turn and followed by `done`; add it to the web client's `KNOWN` set and `ChatStreamEvent` union so it is not silently dropped; verify tests: the SSE ordering test, and a Vitest that the reducer receives the event rather than discarding it
- [x] 5.2 A confirm endpoint that approves or rejects a proposal for the authenticated user: approve re-issues the tool call with the state and returns what was applied; reject applies nothing, tells the agent the user declined as a normal tool result, and lets the turn finish; verify tests: approve applies once, reject applies nothing and the agent answers without re-proposing, a confirmation from another user is refused, and approving twice applies once
- [x] 5.3 Audit every step under a new kind — proposed, reviewed, confirmed, rejected, applied — attributed to the acting user with the account and adjustment as identifiers and no free text; add the new kind and the two missing A2A kinds to `web/src/api/types.ts` and the compliance page's filter; verify tests: a record per step with the chain still verifying, the confirmation naming the approver, no reason or verdict text in any record, and the page offering every kind
- [x] 5.4 Each step as a turn-trace event carrying the adjustment id and, where one exists, the A2A task id; verify a test that a full flow's trace contains the steps in order with strictly increasing sequence numbers

## 6. Evals

- [x] 6.1 Add `propose_fee_adjustment` to the tool allowlist in `src/Maf.Lab.Eval/Datasets/Datasets.cs`, a fake for it in `tests/Shared/FakeTools.cs`, and selection cases: an obvious adjustment picks the write tool, "how do fee adjustments work" picks `search_documents`, and a question about a run still picks the run tool; verify `make eval SUITE=selection` runs and `EvalHarnessTests` still passes with the extended expectations
- [x] 6.2 An injection case asking the assistant to adjust a fee on the strength of embedded text, asserting no answer claims an adjustment was made; verify `make eval SUITE=injection` passes at its threshold
- [x] 6.3 Read the regression comparison for both suites and decide deliberately: either fix the tool description until the metrics hold, or `make eval-accept` with the baseline diff committed; verify the report shows no unexplained regression

## 7. Verification and docs

- [x] 7.1 Live: bring the stack up, propose an adjustment below the threshold and approve it, propose one above and watch the review, answer its question, reject one, approve the same proposal twice, and stop the reviewer mid-flow — record what the stack showed for each
- [x] 7.2 Run `make test`, `make lint`, `make verify`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`, and extend `scripts/verify_lb.sh` with a proposal made against one replica and confirmed through the balancer
- [x] 7.3 Docs: `docs/http-api.md` (the new event and the confirm endpoint), `docs/trace-events.md` (the new steps), README (what the write tool does and that it never writes unasked), `DECISIONS.md` §25 — the retrieval-side store and why not the API's, the signed state over a pending table, MRTR over elicitation, no Workflows package and what would change it, "why one assistant agent", no OpenTelemetry and what the turn trace carries instead, and the threshold's default; verify the sections exist

# Tasks

## 1. What the agents have been doing

- [x] 1.1 `GET /api/admin/a2a` for a FIRM_ADMIN: inbound tasks (partner, operation, task, state, times), outbound consultations (agent, task, outcome, duration) and push deliveries (task, state, attempts, delivered, error), all scoped by the caller's firm through the audit record; verify tests: a partner's task concerning firm A is listed for firm A and not for firm B, a failed consultation is listed with its outcome, a failed delivery says so, and no message content appears anywhere in the response
- [x] 1.2 `POST /api/admin/a2a/tasks/{id}/cancel`: a running task of the caller's firm is cancelled through the same path a partner's cancel takes; verify tests: a working task ends cancelled, a finished one is refused and says so, and a task of another firm is refused
- [x] 1.3 The A2A screen at `/admin/a2a`, reachable by a FIRM_ADMIN only, with the three lists, a cancel button on a running task and a refresh; verify Vitest: the lists render with their states, cancelling calls the endpoint and reflects the result, an advisor does not get the screen, and an empty stack says so rather than showing empty tables

## 2. Conformance as a dataset

- [x] 2.1 `evals/a2a-conformance.jsonl`: one row per scenario — card discovery, extended card after authentication, a direct message reply, a streamed task, resubscribing after a dropped stream, resuming an input-required task, cancelling, push delivery, and a request outside the partner's firms being refused
- [x] 2.2 The probe reads the dataset and runs the scenario each row names, keeping its independence — no project reference to `src/`; verify the probe runs green against the stack and an unknown scenario name fails loudly rather than silently passing
- [x] 2.3 The probe writes a report in the harness's shape (suite, variant, `passRate`, named failures) to `evals/reports/`; verify a test on the report's shape, and `make eval-a2a` plus the end-to-end job running it

## 3. The two fixture sets

- [x] 3.1 `evals/injection-a2a.jsonl`: verdicts a broken or hostile reviewer might send — an embedded instruction, another account, another adjustment, a missing decision, a decision that is neither approved nor refused; verify a data-driven test that drives each through the verdict check and asserts what it is treated as
- [x] 3.2 The same fixtures through the write flow: none of them causes a proposal or an application for anything they name; verify a test that a verdict naming another account leaves that account untouched and proposes nothing for it
- [x] 3.3 `evals/ui-events.jsonl`: runs captured from the running stack — one plain answer, one with a tool call and sources, one that pauses for a confirmation — each with the state the browser should reach; verify Vitest that replaying each produces that state

## 4. Verification and docs

- [x] 4.1 Live: drive the stack with the probe, watch the A2A screen fill with what it did, cancel a running task from the screen, and capture the recorded runs the reducer tests use — record what the stack showed (all 10 scenarios green; the screen listed the probe's tasks, consultations and 2 push deliveries; a Working task cancelled from the button read Canceled with an `a2a.cancel` audit row; three runs captured)
- [x] 4.2 Run `make test`, `make lint`, `make verify`, `make eval-a2a`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`
- [x] 4.3 Docs: `docs/http-api.md` (the A2A admin endpoints), README (the screen, and that conformance is run by an outside client), `DECISIONS.md` §28 — the audit as the scope rather than a new column, why the conformance runner stays in the probe, why `make eval` does not run it, and why the hostile verdicts are a fixture set rather than a model suite; verify the sections exist

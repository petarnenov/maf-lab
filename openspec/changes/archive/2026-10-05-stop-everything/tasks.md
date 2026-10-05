# Tasks

Starts after `esc-stops-chat-run` is archived (it brings `TaskCancelWatch` and the Redis terminal guard). No Jev call
is added or changed, so the Jev review checklist does not apply.

## 1. Stores that keep a stop

- [x] 1.1 Admin jobs: add `canceled` to the job states; every state change on `AdminJobs` becomes one conditional
      `UPDATE … WHERE State NOT IN (terminal)`. Verify with xUnit: cancel then `succeeded` → stays `canceled`;
      `running` then `succeeded` → `succeeded`; the one-running-job-per-kind index still holds.
- [x] 1.2 `SqliteTaskStore.SaveTaskAsync` keeps a terminal state against a different incoming one, in one statement.
      Verify with xUnit: `canceled` then `working`/`completed` → `canceled`; `working` then `completed` → `completed`.

## 2. Workers that watch

- [x] 2.1 `JobCancelWatch` for admin jobs (polls the row every second, cancels a linked token). Verify with unit tests:
      cancels on `canceled`, not otherwise; stops when disposed.
- [x] 2.2 `AdminJobRunner` runs each job under `ApplicationStopping` linked with the watch; a job that stops on it
      ends `canceled` with how far it got. Add `POST /api/admin/jobs/{id}/cancel` (admin, firm-scoped; 202 / 409).
      Verify with xUnit over two api hosts on one database: start on one, cancel through the other → `canceled`, the
      work stops, still `canceled` later; a viewer gets 403; an ended job gets 409.
- [x] 2.3 `BillingAgentHandler` runs its stages under `TaskCancelWatch`. Verify with xUnit over two api hosts: a
      billing run cancelled through the other replica ends `canceled` before its next stage and stays so.
- [x] 2.4 Coverage runner: `POST /runs/{id}/cancel` (caller count for joined jobs; kill the tree; remove the
      workspace; never store a cancelled result for reuse); `CoverageRunnerClient` cancels when its token fires;
      coverage refresh cancels its runner job. Verify with xUnit: a hanging build cancelled → process tree gone,
      workspace gone, `canceled`; a job joined by two callers survives one cancel.

## 3. Safe points

- [x] 3.1 Indexing: shield the document in hand (`ReplaceDocumentAsync` under `CancellationToken.None`), check the
      token before the next; graph build and `rebuild-index` say what to run again when stopped. Verify with
      integration tests: cancelled mid-run → no half-written document, the summary names the count and the command.
- [x] 3.2 Candidate accept/discard checks the token before the merge or delete, not during it. Verify with xUnit.

## 4. Stores stop their part

- [x] 4.1 `VerifyConnectivityAsync` gets the caller's token (both sites). Verify the build and the topology tests.
- [x] 4.2 Integration test against the Testcontainers Neo4j: a long read cancelled mid-run ends at once and
      `SHOW TRANSACTIONS` no longer lists it; if it does, end it on cancel through the driver's session close and
      re-verify. Record the finding in design.md.
- [x] 4.3 Integration test against the Testcontainers Qdrant: a query cancelled mid-call ends at once with a cancelled
      gRPC status.

## 5. The web

- [x] 5.1 `useEscToStop` and the shared stop hint; move the chat page onto them. Verify that the chat stop tests still
      pass unchanged.
- [x] 5.2 Every `queryFn` passes react-query's `signal` to the request (mutations get none from react-query; the
      ones here are short POSTs whose started work is stopped through its own cancel route). Verify with a Vitest test that
      unmounting a page aborts its request, and that `npm test` passes.
- [x] 5.3 Index admin: Esc stops an index run and a migration through the cancel route; "Stopping…", then canceled
      with the count; Esc aborts a loading drift report. Verify with Vitest per `web-ui` scenario.
- [x] 5.4 Coverage: Esc in the Activity dialog cancels for an administrator (viewer: closes); Esc stops a coverage
      refresh. Update `RunActivity.test.tsx:350` to the new behaviour. Verify with Vitest per `coverage-dashboard`
      scenario.
- [x] 5.5 Long reads (topology probe, compliance verify/export, code snippets) stop on Esc and on leaving. Verify with
      Vitest.

## 6. The CLI

- [x] 6.1 `Maf.Lab.Eval` and `Maf.Lab.A2AProbe`: SIGINT/SIGTERM cancel the root token, one final line, exit 130.
      Verify with a test that starts the tool, sends SIGINT and reads the exit code and last line.
- [x] 6.2 `tools/screenshots/capture.mjs` and `copilot-runtime/conformance.mjs`: close what they opened on SIGINT,
      exit 130. Verify by hand: Ctrl+C mid-run leaves no browser process.
- [x] 6.3 `coverage_refresh.sh`, `testgen_e2e.sh`, `verify_lb.sh`: trap INT/TERM, cancel the job or run they started,
      restore a stopped replica, exit 130. Verify by hand with Ctrl+C during each, then check the job reads
      `canceled`.
      Done: `make coverage` (refresh job and its runner job end canceled, exit 130) and `make verify` interrupted during
      its index job (job canceled, the stopped replica started again, exit 130). `testgen_e2e.sh` was not interrupted
      live: it is `make ci-e2e`'s and acts on the repository it is given, so it was not run against this checkout; its
      handler is the same as `coverage_refresh.sh`'s.

## 7. Checks

- [x] 7.1 Run `make lint` and `make test`, and verify both pass.
- [x] 7.2 On the rebuilt stack: Esc on an index run, a coverage refresh, a test-generation run (Activity dialog) and a
      slow drift report; each says "Stopping…" then stopped, and the server logs show the work ended (job `canceled`,
      runner job cancelled, request aborted). Ctrl+C during `make index` and `make coverage`.
      Done live: Esc on an index run (job canceled; it had finished its unchanged corpus within the watch's second, so
      its own end was refused rather than its work cut — the cut is shown by the two-replica tests); Esc on a coverage
      refresh ("Stopping…", then "The refresh was stopped", `admin job coverage.refresh canceled`, `runner job
      canceled`); Ctrl+C on `make coverage` and `make verify`; the indexer, eval and A2A probe CLIs signalled for real.
      Not live: a test-generation run from the Activity dialog (it calls the model and branches the working repository;
      covered by Vitest) and a slow drift report (it answers at once on this stack; covered by Vitest).

## 8. Documentation

- [x] 8.1 `CLAUDE.md`: the non-negotiable, next to progress feedback.
- [x] 8.2 `openspec/project.md` Conventions and `.github/copilot-instructions.md`: the rule.
- [x] 8.3 Run `make docs` (the config's project context and the HTTP route tables), then `make docs-check`, and verify
      it passes.
- [x] 8.4 Run `npx --yes @fission-ai/openspec@1.13.1 validate stop-everything --strict` and verify it passes.

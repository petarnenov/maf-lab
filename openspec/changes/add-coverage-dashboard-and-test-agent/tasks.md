# Tasks

## 1. Coverage tooling and ingestion (backend)

- [x] 1.1 Add `Microsoft.Testing.Extensions.CodeCoverage` to `Directory.Packages.props` and `tests/Maf.Lab.Tests`,
  and write a coverage config that includes `src/**` modules only. Note it in DECISIONS.md: "coverage on MTP, why not
  coverlet". Verify that `dotnet test --project tests/Maf.Lab.Tests -- --coverage --coverage-output-format cobertura`
  produces a Cobertura file.
- [x] 1.2 Add `@vitest/coverage-v8` and a `test.coverage` block in `web/vite.config.ts` (v8, cobertura plus
  text-summary, include `src/**`, exclude tests and `src/test`), and note it in DECISIONS.md. Verify that
  `npx vitest run --coverage` writes `coverage/cobertura-coverage.xml`.
- [x] 1.3 Add the SQLite tables from design D10 (`CoverageSnapshots`, `CoverageFiles`, `CoverageThresholds`,
  `TestGenRuns` with the partial unique index, `TestGenRunEvents`) to `MafDbContext` and `DatabaseInitializer`.
  Verify with a test that initialising twice is idempotent and that a second non-final run for the same path violates
  the index.
- [x] 1.4 Implement `CoberturaParser`: parse into the normalised model, handle `condition-coverage` for partial lines,
  and reject malformed XML. Verify with xUnit tests on fixture reports from both toolchains, including a partial-branch
  line.
- [x] 1.5 Implement `CoveragePaths.Normalise`: resolve absolute or source-relative paths to repo-relative ones, drop
  and count paths outside the repo, and exclude test and generated files. Verify with tests for the `/src/src/...`
  case, the `web/src`-relative case, the outside-repo case and the traversal case.
- [x] 1.6 Implement `CoverageStore`: ingest a snapshot (kind, commit, dirty, toolchain), keep the latest official
  snapshot per file, handle candidates linked to runs, and promote a candidate. Verify with tests for "latest per
  file", "candidate does not replace official" and "promotion".

## 2. Coverage API

- [x] 2.1 Map `/api/coverage` in `Endpoints/CoverageEndpoints.cs` with `GET /tree`. Aggregate folders weighted by
  lines, add effective thresholds, the candidate and the active run. Verify with a WebApplicationFactory test for the
  10-line/90-line = 10% case and a 401 without a token.
- [x] 2.2 Add `GET /files?path=` (source from `git show <sha>:<path>`, only for paths in a snapshot) and
  `GET /files/history?path=`. Verify with tests for the detail, newest-first history and `../../etc/passwd` → 404.
- [x] 2.3 Add `POST /reports` (admin, multipart Cobertura with commit and toolchain). Verify with tests for admin
  success, non-admin 403 and malformed 400 with nothing stored.
- [x] 2.4 Add the `CoverageRunnerClient` (typed HttpClient, service token) and `POST /refresh` (admin, both toolchains
  at `main`, single-flight through a Redis lock returning the in-progress refresh). Verify with a test against a fake
  runner, where two concurrent refreshes start one run.

## 3. Web: Coverage screen, tree and file view

- [x] 3.1 Add the `/coverage` route and the nav link in `LINKS`, and API types and helpers in `api/`. Verify with a
  Vitest test that the nav shows Coverage and the route renders the page.
- [x] 3.2 Build `CoverageTree`: folders with aggregates, per-file % and threshold, a below-threshold marker (icon plus
  text, theme colours), a candidate badge, sort by name or coverage, a path filter and a below-threshold toggle.
  Verify with RTL tests for sort, filter, toggle and marker.
- [x] 3.3 Build `FileView`: a summary header, line status classes, a hit and branch tooltip on hover and focus, and a
  windowing hook (fixed line height, overscan). Verify with RTL tests that a 5 000-line file renders at most about
  (viewport + overscan) line elements and that a partial line shows "1/2 branches".
- [x] 3.4 Add loading, empty ("No coverage report yet", plus Refresh for admin) and region-local error states, with
  no internal text. Verify with RTL tests for each state and that a failed file view leaves the tree usable.

## 4. Web and API: threshold and model picker

- [x] 4.1 Add `Coverage:DefaultThresholdPct` (80) and `PUT /thresholds?path=` (admin): save when the value is lowered,
  kept, cleared or already met; return `409 run_required` when raised above coverage; return 400 when out of range;
  return 409 when a run is active. Verify with endpoint tests for each case.
- [x] 4.2 Add `TestAgent:Models` configuration with the five allowlisted models (`glm-5.3:cloud` as default,
  `PriceIsEstimate: true`), `ModelAvailability` (a 1-token probe with no repo content and a 10-minute Redis cache),
  and `GET /models` with estimate inputs. Verify with tests that a provider refusal marks a model unavailable and that
  the chat model setting is unchanged.
- [x] 4.3 Add `CostEstimator` (design D13). Verify with unit tests that the estimate changes with the model and scales
  with the remaining attempts.
- [x] 4.4 Build `ThresholdControl`, `RaiseThresholdDialog` (current %, target %, "an agent run will start") and
  `ModelPicker` (name, price labelled estimate, best-for, unavailable disabled, cost estimate and cap, Start disabled
  until a model is selected). Show admin-only controls, and lock the control with run status while a run is active.
  Verify with RTL tests for the lowered flow, raised-but-met, cancel at the dialog, cancel at the picker (threshold
  unchanged), an unavailable model not selectable, and the locked state.

## 5. Test-generation agent service

- [x] 5.1 Check whether `Microsoft.Agents.AI.Hosting.A2A` 1.22.0-preview now exposes `A2AAgentHandler` and
  `AddA2AServer` publicly and can express `rejected`. Record the finding in DECISIONS §23. Proceed with
  `Maf.Lab.A2A` either way, as design D2 says. Verify that the DECISIONS entry exists.
- [x] 5.2 Create `src/Maf.Lab.TestAgent` (Web SDK, added to the solution): `MapA2ASurface` and `MapA2AProtocol`,
  Redis task and push stores, `AddLabTelemetry("maf-lab-test-agent")`, `/health`, a card with the one skill
  `generate-tests`, and `A2A:Audience` `maf-lab-test-agent`. Verify with a test that the card is served and that a
  task without a token gets 401.
- [x] 5.3 Implement `WorkspacePaths.Resolve` (design D7) in a shared place used by the agent and the api. Verify with
  tests for the production path, `tests/../src`, absolute paths, a symlink escape, `.git/`, and allowed test paths for
  both toolchains.
- [x] 5.4 Implement `TestGuardrails.Check` (design D8): Roslyn for C#, and the TS compiler API through a node script
  for TS. Verify with fixture tests for `Skip=`, `.only`, `xit`, an assertion-free test, a swallowed expected
  exception, and a clean test that passes.
- [x] 5.4a Extend `TestGuardrails` for suspected bugs (design D19): allow skips only when they carry a
  `suspected-bug:` marker and are listed, reject more than 3, and flag test code that writes, moves or deletes files
  under `src/` or `web/src/`. Verify with fixture tests for an allowed suspected-bug skip, an unlisted skip, a
  fourth bug, and `File.WriteAllText("src/…")` / `fs.writeFileSync('web/src/…')`.
- [x] 5.5 Implement the scratch workspace (a read-only clone at the SHA) and the five tools (design D6) as
  `AIFunction`s returning DTOs. Verify with tool tests for the caps, refusal text and write-then-read.
- [x] 5.5a Add the `report_suspected_bug` tool and `suspectedBugs[]` in the report. The instructions tell the model to
  assert intended behaviour, never to change production code or bend an assertion to observed behaviour, and to skip
  with the marker and report. Verify with a scripted run in which a failing test is skipped and reported, and the
  report carries it.
- [x] 5.6 Implement `TestGenerationHandler`:
  - input validation, which rejects over 5 attempts, a missing file or an incomplete input;
  - the code-owned loop;
  - feedback assembly;
  - budget stop before and during an attempt;
  - progress updates;
  - cancellation;
  - the final `testgen.report/v1` artifact, with a diff cap of 256 KB.

  Verify with `ScriptedChatClient` plus a fake runner, covering: target reached at attempt 2, goal not reached after
  5, budget stop, cancel during attempt 3 with no further calls, `model_unavailable` → failed, and an invalid input
  → rejected.
- [x] 5.7 Check that no log, span or artifact contains a prompt, source text or the API key. Verify with a test using
  an in-memory exporter and log sink that asserts none of the fixture's source lines or key appear.

## 6. Coverage runner service

- [x] 6.1 Create `src/Maf.Lab.CoverageRunner` with `POST /runs` (commit, toolchain, diff?, target?), service-token
  auth, a fresh workspace per request (`git worktree add --detach` from the read-only repo into its own work dir),
  `git apply --check` and apply, build and test with coverage to a nonce path, and a result DTO. Verify with tests on
  a fixture repo for a diff that applies, a diff that does not apply (no build), and a clean workspace between runs.
- [x] 6.2 Add the time limit, bounded concurrency with a queue position, and the non-root execution. Verify with tests
  that a hanging test is stopped and reported timed out, and that the second request reports queue position 1.
- [x] 6.3 Add a Dockerfile: SDK 10.0.401, Node 24 and git, with NuGet and npm restores pre-seeded at build and a
  non-root user. Verify that the image builds and that a run inside it succeeds with `--network none`.

## 7. A2A wiring and run orchestration (api)

- [x] 7.1 Add `TestAgentOptions` and a `TestAgentClient` that reuses the consultant's token and card helpers. Start
  runs through `A2AAgent` with background responses and persist the task id. Use `A2AClient` for
  get/resubscribe/cancel if `A2AAgent` lacks them, and record the gap in DECISIONS §23. Add `ToolAudit` records
  `a2a.testgen.*`. Verify with tests against an in-process fake agent for start and for unreachable → 503 with no
  active run.
- [x] 7.2 Implement `POST /runs`:
  - checks: admin, model in the allowlist and available (otherwise 422), and no active run (otherwise 409);
  - saves the threshold and the run in one transaction only after the task is accepted;
  - leaves the threshold unchanged when the agent is unreachable.

  Verify with endpoint tests for each case.
- [x] 7.3 Implement the `RunFollower` background service:
  - Redis lease per run;
  - subscribe, resubscribe, then fall back to polling `tasks/get`;
  - persist events before relaying;
  - cancel on deadline;
  - lease takeover after a replica dies.

  Verify with tests for a stream drop mid-attempt with no lost events, an expired lease taken over by a second
  follower, and deadline → `failed(deadline)`.
- [x] 7.4 Add `GET /runs/{id}/events` (SSE: a snapshot first, then progress and state, closing on a final state,
  Redis pub/sub with a poll fallback), `GET /runs` and `GET /runs/{id}`, and `POST /runs/{id}/cancel`. Verify with
  tests that a late subscriber gets the current state first and that cancel mid-run ends the run `canceled` with no
  branch.
- [x] 7.5 Web: add `useRunEvents` (fetch plus `SseParser`) and wire it to the tree, the control and the file header.
  Invalidate tree, file and runs queries on a final state. Verify with a Vitest test using a scripted SSE stream.

## 8. Verification, candidate branch and accept

- [x] 8.1 Implement `RunVerifier`, which runs when the task completes:
  - allowlist check on every diff path, including renames;
  - `TestGuardrails.Check`;
  - a runner run in a fresh workspace;
  - build ok and zero failures required;
  - ingest a candidate snapshot on success, otherwise `verification_failed(reason)`;
  - an empty diff gives `completed_no_change`.

  Verify with tests for a failing test → `verification_failed` with coverage unchanged, a production path in the diff
  → `verification_failed` with no runner call, and reported 88% vs measured 84% → candidate 84%.
- [x] 8.1a Add `GitHubIssues` (typed HttpClient, `GITHUB_ISSUES_TOKEN` from the environment only, repository from
  `GitHub:Repository` or origin) and the `TestGenIssues` table. In `RunVerifier`, check every skip against the
  suspected bugs, prove each by an un-skipped runner run, create the issue once per run and test (surviving a
  restart), and rewrite the marker with the issue link, or with "no issue" when there is no token. Verify with tests
  against a fake GitHub: a confirmed bug gives one issue and a linked marker; a bug not reproduced ends
  `verification_failed` with no issue; an unlisted skip fails; verifying twice creates one issue; no token gives the
  "no issue" marker.
- [x] 8.2 Add `git` to the api image and implement `RepoWriter.CreateBranch` (a temporary detached worktree, apply,
  commit as `maf-lab test-agent`, branch `test-agent/<slug>-<runId>`, Redis lock). Verify with a test on a temporary
  git repo that the branch has exactly one commit, contains exactly the diff, and leaves the checkout untouched.
- [x] 8.3 Implement `RepoWriter.Merge`:
  - refuse when `main` is checked out in a dirty worktree;
  - in a clean checkout, `merge --no-ff` (with `--abort` on conflict);
  - otherwise `merge-tree`, `commit-tree` and CAS `update-ref`, retried once;
  - `POST /runs/{id}/accept` promotes the candidate at the merge commit;
  - `POST /runs/{id}/discard` deletes the branch.

  Verify with temporary-repo tests for a clean accept, a conflict (main unchanged, run stays candidate), a dirty
  checkout (refused, nothing written), a CAS retry after main moves, and discard.
- [x] 8.3a On Accept, comment on each run issue with the merge commit. On Discard, close each with a comment. A GitHub
  failure is shown and does not block. Verify with fake-GitHub tests for both, and for GitHub down.
- [x] 8.4 Web: add a candidate panel in the file view (diff summary, candidate %, suspected bugs with issue
  links, Accept and Discard with a confirmation, and the refusal reason shown). Verify with RTL tests for accept
  success, conflict and dirty refusals, and a listed bug with its link.

## 9. Compose, topology, Make and telemetry

- [x] 9.1 Add the `test-agent` and `coverage-runner` services, the `runner` network (`internal: true`) and the
  volumes. Give the api read-write access to `${MAF_LAB_REPO}` and a place on `runner`, give the collector a place on
  `runner`, and point the CI compose at the Ollama stub with a scripted test answer. Verify that `make` brings
  everything up healthy and that `docker compose exec coverage-runner env` shows no `OLLAMA_API_KEY` or `JEV_MAF_LAB`.
- [x] 9.2 Export `MAF_LAB_REPO` and the host UID and GID from the Makefile, add the `coverage` target (with help text,
  a non-zero exit when the stack is down), and extend `make doctor` to check `MAF_LAB_REPO` and to report the optional `GITHUB_ISSUES_TOKEN` without printing it.
  Pass `GITHUB_ISSUES_TOKEN` to the api service only. Verify that `make help`
  lists `coverage` and that `make coverage` with the stack down exits non-zero.
- [x] 9.3 Add `TestAgent` and `CoverageRunner` to `TopologyOptions`, add their probes, ids and edges in
  `TopologyProbe`, and add their boxes to `docs/topology.drawio` without overlap. Verify that `TopologyTests` pass
  (every node drawn, no overlaps).
- [x] 9.4 Add `testgen.run`, `testgen.attempt` and `runner.run` spans with structural attributes only, with trace
  context carried across the api, agent and runner. Verify with a test that a run through fakes yields one trace id
  across all three services' spans.

## 10. End-to-end and checks

- [x] 10.1 Add an end-to-end test (in `ci-e2e`) for a fixture file `src/Maf.Lab.Api/Coverage/Fixtures/E2eTarget.cs`
  at low coverage. It raises the threshold, confirms, picks the stub model, runs, verifies, creates a candidate
  branch, accepts, and checks that official coverage for the file is at or above the target. Verify that
  `make ci-e2e` passes.
- [x] 10.2 Run the Jev review checklist (docs/rules/jev-usage.md §7) against this change. Record in the change that no
  Jev call is added or modified, so no labeled-input set (Bulgarian included) is needed. Verify that the note exists
  in `tasks.md` under this item.
  - **Jev review note (2026-09-30).** No Jev request is added or changed. Checked against §7: none of the
    change's decisions is a closed-set judgment over natural language. Threshold against coverage, the allowlist,
    the path allowlist, guardrail detection, stop conditions, and whether a suspected bug reproduces are all decided
    by code (§2 "code controls"). The tests are written by the model (§2 "LLM writes"). Side effects (writes, merges,
    issues) are enforced in code. The remaining items (question design, confidence gating, fallback, pinned
    version, 429/401 handling, labeled Bulgarian inputs) apply only to a Jev call, and there is none, so no labeled
    input set is needed. `git grep` over the new projects finds no call to `/v1/systemone`.

- [x] 10.3 Run `make lint`, `make test`, `make verify` and `openspec validate add-coverage-dashboard-and-test-agent
  --strict`. Verify that all pass.
  - **Result (2026-09-30).** `make lint` passes, and so do `make verify` on the normal stack and `openspec validate
    --strict`. `make ci-e2e` passes too, including test generation end to end on a clone. `make test`: 998/999 .NET
    and all web tests pass. The one failure, `GuardrailTests.A_hanging_Jev_costs_no_more_than_the_timeouts`, is a
    wall-clock bound that also fails on `main` (1 of 3 full runs, at 3.86 s), as does
    `A2AStreamingTests.A_dropped_stream…` (2 of 3). Both predate this change.

# Proposal

## Why

A person who starts something must be able to stop it. Today only the chat answer stops on Esc, and test-generation
runs and A2A partner tasks have a Cancel button. Nothing else stops:
- admin jobs (index, migrate, coverage refresh) have no cancel at all and run until the replica shuts down;
- the coverage runner's jobs go on after their caller gives up;
- no browser request is ever aborted (`apiRequest` takes a `signal`, but no caller passes one);
- the api's own A2A billing loop never sees a cancel, and its task store lets a late write undo one;
- several CLI tools ignore Ctrl+C, or leave the server work they started running.

The rule becomes a first-class convention of the project, next to progress feedback: **everything can be stopped** —
with Esc on the page that started it, with Ctrl+C in a terminal — and a stop reaches every party doing the work, by
each protocol's own means.

## What Changes

- **A project rule, `stop-anything`**, written into `CLAUDE.md` (non-negotiables, next to progress feedback) and
  `openspec/project.md` (Conventions), with its own spec, checked on every proposal the way progress feedback is.
- **The stop path is each protocol's own, nothing invented:** AG-UI through CopilotKit's stop (chat, test-generation
  following); an HTTP request's abort (browser → api → MCP `tools/call` → Qdrant gRPC / Neo4j Bolt, all already on one
  `CancellationToken`); A2A `tasks/cancel` for agent tasks.
- **Work that outlives its request is stopped through the store that owns its state**, atomically, and the worker
  running it watches that store — no sticky routing, any replica may receive the stop:
  - A2A agent tasks (compliance, test agent): Redis, from `esc-stops-chat-run`;
  - the api's A2A tasks (billing agent): `SqliteTaskStore` gets the same terminal guard, and the billing loop watches;
  - admin jobs: a new `canceled` state and a cancel endpoint; the shared `AdminJobs` row is the stop (one conditional
    update), and the owning replica's job watches its row;
  - coverage runner jobs: a cancel endpoint that kills the job's process tree; the api cancels the runner job when the
    work that asked for it is stopped.
- **The UI:** on every page that starts work longer than a moment, Esc stops it — admin index run and migration,
  coverage refresh, test-generation run, candidate accept/discard (before it starts its indivisible step), long reads
  (drift report, topology probe, compliance verify/export, code snippets). Every request the web makes carries the
  abort signal react-query (or the page) holds, so leaving a page or pressing Esc aborts the request on the server
  too. The page says "Stopping…" until the server confirms, then the stopped outcome in its own design
  (progress-feedback).
- **The CLI:** every tool handles Ctrl+C: it stops at a safe point (between items, never half-way through a document
  or a batch it must write whole), says what was done and what to run again, exits 130, and cancels any server work it
  started (scripts that start admin jobs or test-generation runs cancel them on the way out).
- **The stores stop too:** tests against real Qdrant and Neo4j containers show a cancelled call ends at once and, for
  Neo4j, that the transaction is gone from the server; the two calls without a token (`VerifyConnectivityAsync`) get
  one.
- **BREAKING (UI):** in the coverage Activity dialog, Esc now stops the run it shows (when the person may stop it)
  instead of only closing the dialog; the dialog keeps its close button.

## Capabilities

### New Capabilities

- `stop-anything`: the project rule — what can be stopped, how (Esc, Ctrl+C), the protocol-only stop path, stopping
  work that outlives its request through its own store, what a stopped thing says, and that proposals are checked
  against it.

### Modified Capabilities

- `coverage-dashboard`: Esc in the Activity dialog stops the run instead of only closing it; the coverage refresh can
  be stopped.
- `web-ui`: the index administration screen's runs and migration can be stopped with Esc; long reads abort.
- `coverage-runner`: a job can be cancelled; its process tree is killed and its workspace removed.
- `a2a-hosting`: the api's A2A task store keeps a terminal state against a late write, and a running billing task
  stops when it is cancelled through any replica.
- `make-workflow`: make targets and scripts stop cleanly on Ctrl+C and cancel the server work they started.

## Impact

- Web: `api/client.ts` callers and every `queryFn`/`mutationFn` pass the abort signal; a shared `useEscToStop` hook
  for pages (the chat page's listener moves into it); index admin, coverage, compliance, topology and code-snippet
  views; `coverage/RunActivity.tsx`.
- api: `AdminJobRunner` (cancel, `canceled` state, a watch on its row), admin and coverage endpoints (cancel routes),
  `SqliteTaskStore` (terminal guard), `BillingAgentHandler` (watch), `CoverageRunnerClient` (cancel the runner job).
- Coverage runner: a cancel route on its own API; `JobQueue` cancels the job's token.
- Shared A2A: `TaskCancelWatch` from `esc-stops-chat-run`, reused.
- CLI: `Maf.Lab.Indexing` (safe stopping points, a summary of what to rerun), `Maf.Lab.Eval` (top-level cancel →
  exit 130), `Maf.Lab.A2AProbe` (Ctrl+C), `tools/screenshots`, `copilot-runtime/conformance.mjs`, and the scripts
  that start server work (`coverage_refresh.sh`, `testgen_e2e.sh`, `verify_lb.sh`) trap INT/TERM and cancel it.
- Retrieval: `VerifyConnectivityAsync` gets a token.
- Tests: per surface (Vitest for Esc; xUnit for cancels across replicas; Testcontainers Qdrant and Neo4j for stores;
  CLI tests that send SIGINT).
- Depends on `esc-stops-chat-run` (its shared-store cancel for A2A agents and `TaskCancelWatch`), which lands first.
- No package moves, no new AG-UI event, no event or request outside the protocols'. No new long process; every stop
  shows "Stopping…" and then its outcome, per progress-feedback.

## Documentation impact

- `CLAUDE.md`: a non-negotiable, next to progress feedback — everything can be stopped (Esc on the page, Ctrl+C in a
  terminal), only by the protocols' own means, long-lived work through its store.
- `openspec/project.md`: a Conventions bullet for the rule (its copy in `openspec/config.yaml` is a generated block,
  rewritten by `make docs`).
- `.github/copilot-instructions.md`: the same rule among its conventions.
- `docs/` HTTP API reference: the new cancel routes for admin jobs and the coverage runner (route tables are generated —
  `make docs`).
- README.md: the keys section, if it lists keys; otherwise none.

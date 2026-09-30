# Proposal

## Why

The lab has no view of its own test coverage. Raising coverage file by file is repetitive work that an agent can do.
The lab already speaks A2A in both directions, so it is a natural place to exercise a long-running, tool-using agent
end to end: a real loop, real feedback, real cost, and an independent check of what the agent claims. This change adds
a Coverage screen and an internal test-generation agent. The agent writes missing tests for one file at a time until
the file meets its threshold. The lab verifies the result itself before anything reaches `main`.

## What Changes

- New **Coverage screen** (`/coverage`, in the main nav). It shows the repo's C# and TS source tree with line coverage
  per file and aggregated per folder. A file view shows the source with per-line `covered | uncovered | partial`
  status and hit counts.
- **Coverage ingestion.** Cobertura reports from the .NET unit tests and from Vitest are normalised into one model
  (file → lines → hits, branches) with repo-relative paths. Snapshots are stored with commit SHA and timestamp. A
  snapshot is produced on demand ("Refresh coverage", `make coverage`) or uploaded.
- **Per-file thresholds.** There is a global default and an optional override per file. Lowering a threshold saves it
  at once. Raising it above current coverage goes through confirmation, a model choice and a cost estimate, then starts
  an agent run.
- **Model picker for the agent.** The models come from a backend allowlist of Ollama Cloud models with prices. The
  initial list is `glm-5.3:cloud` (default), `kimi-k3:cloud`, `glm-5.3-flash:cloud`, `deepseek-v4-pro:cloud` and
  `deepseek-v4.1-flash:cloud`. The backend validates the choice and checks the model is available before a run starts.
  The chat assistant's model (`gpt-oss:120b`) is not touched.
- **Test-generation agent.** A new MAF (C#) service in its own container, reached only over A2A as a long-running
  task. Each attempt generates or modifies tests, then builds, runs and measures. The agent stops at the target or
  after 5 attempts. Its tools can write only test files. Guardrails forbid skipped, focused and assertion-free tests.
  It returns a structured report and a unified diff as an A2A artifact.
- **Coverage runner.** A new container with the .NET SDK and Node toolchains, no secrets and no internet access. It
  builds and runs tests with coverage for a given commit plus a diff. The agent uses it for attempts. The api uses it
  separately for independent verification.
- **Test-generation runs.** The api is the A2A client. It starts the task, follows its progress (stream, with polling
  as a fallback), relays it to the browser over SSE, and allows cancelling. When the run completes it re-checks the
  guardrails, re-runs the tests itself, commits the diff to a branch `test-agent/<file>-<runId>`, and shows the new
  coverage as a *candidate*. **Accept** merges the branch into `main` (refused if `main` moved in a conflicting way
  or its checkout is dirty), and the candidate becomes the file's coverage.
- **Cost estimate and budget.** The estimated cost is shown before start. Each run has a hard token and cost cap. The
  actual tokens and cost are in the report.
- **Observability.** One trace spans browser → api → A2A → agent → runner and LLM calls, using the existing telemetry
  conventions. No source code or prompts go into telemetry.
- Topology report and diagram gain the two new services. The Make catalogue gains `coverage`.

## Capabilities

### New Capabilities

- `coverage-dashboard`: the Coverage screen, with a tree of files and folders, coverage and threshold flags, sort and
  filter, a virtualised file view with line status, and loading, empty and error states.
- `coverage-ingestion`: normalising Cobertura from both toolchains into one model, snapshots keyed by commit, the
  latest snapshot per file, candidate snapshots, and the tree, file and history API.
- `coverage-threshold`: global default and per-file override, the save-or-run decision, the confirmation flow, and
  one active run per file with the control locked meanwhile.
- `model-selection`: the configured allowlist with prices and notes, a single selection, server-side validation and
  availability check, and the cost estimate before start.
- `test-generation-agent`: the A2A agent's input, the attempt loop, tools with a test-only write allowlist,
  guardrails, stop conditions, budget, and the report and diff artifact.
- `coverage-runner`: an isolated build-and-test-with-coverage service used by both the agent and the api. It holds
  no secrets and has no internet access.
- `test-generation-runs`: the api side of a run. It covers the A2A task lifecycle and UI states, the SSE relay,
  cancel, independent verification, the candidate branch, Accept or Discard, and trace propagation. It is named for
  the feature rather than `a2a-integration`, so it is not confused with the general `a2a-client` capability, whose
  rules (card discovery, service credentials, deadline, audit) it follows unchanged.

### Modified Capabilities

- `system-topology`: the report and diagram must include the test-generation agent and the coverage runner, with
  their edges.
- `make-workflow`: the target catalogue gains `coverage`, which refreshes the coverage snapshot.

## Impact

- **New projects**
  - `src/Maf.Lab.TestAgent` (A2A server, MAF agent, Dockerfile).
  - `src/Maf.Lab.CoverageRunner` (runner HTTP service, Dockerfile with SDK and Node).
- **Api changes**
  - New `Coverage/` feature: ingestion, store, and threshold and run endpoints under `/api/coverage`.
  - An A2A client for the test agent.
  - A repo writer (git) used for the candidate branch and the merge.
  - New SQLite tables added through `DatabaseInitializer`.
- **Web changes**
  - New `coverage/` feature: page, tree, virtualised file view, threshold control, confirm dialog, model picker, and
    run status over SSE.
  - A nav link.
  - `@vitest/coverage-v8` added.
- **Tests and packages**
  - Code coverage for .NET moves to the Microsoft.Testing.Platform coverage extension. The tests already run on
    MTP, so the VSTest-based coverlet collector named in the brief would not attach.
  - Possibly `Microsoft.Agents.AI.Hosting.A2A`. Every version move is recorded in DECISIONS.md.
- **Compose**
  - Two new services.
  - The repo is bind-mounted: read-only for the agent and the runner, read-write for the api only.
  - The runner sits on an internal-only network.
  - `OLLAMA_API_KEY` is passed to the test agent. The runner gets no secrets.
- **Topology:** `TopologyOptions`, `TopologyProbe` and `docs/topology.drawio` gain the two new nodes.
- **Jev:** no Jev call is added or changed. Every decision in this change (threshold vs coverage, the allowlist,
  guardrail detection, stop conditions) is either deterministic, so code decides, or generative, so the LLM writes.
  None is a closed-space judgement over natural language, so Jev is not the right tool here
  (docs/rules/jev-usage.md §2 and §5).
- **Tenancy:** coverage, thresholds and runs describe the repository, not tenant data. No `firm_id` is involved and no
  tenant parameter is added anywhere. Starting, cancelling and accepting runs require the FirmAdmin policy.
- **Security:** code written by the model runs only in the runner, which has no secrets and no egress. The api
  re-checks the diff's paths and guardrails and never trusts the coverage numbers the agent reports.

## Open Questions

- **Prices.** What per-1M-token prices (input and output) should the allowlist carry? Ollama Cloud bills by plan. Until
  someone confirms, the configured values are labelled "estimate" in the picker and the report.
- **Model availability.** DECISIONS §9 found most cloud models unavailable on this account's tier. Which of the five
  allowlisted models does the account actually serve? Unavailable ones show as disabled (see `model-selection`), but a
  list that is entirely disabled would make the feature unusable.
- **CI.** Should CI publish Cobertura artifacts and upload them to ingestion? The upload endpoint is part of this
  change; wiring CI to it is not.
- **Retention.** How long should old snapshots and runs be kept? For now nothing is pruned.
- **Default threshold.** The global default is assumed to be 80% line coverage.

## Out of scope (follow-ups)

These items from the brief's recommended additions are not in this change:

- Mutation-score gate.
- Automatic model escalation.
- Batch mode.
- Model scorecard.

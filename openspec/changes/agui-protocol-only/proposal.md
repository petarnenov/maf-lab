# Proposal

## Why

The rule for the wire was "AG-UI and nothing else", but the code did not follow it. §26 allowed custom events, and every
change since has added its own. The chat has `maf-lab/sources` and `maf-lab/trace`. The test-generation run has
`maf-lab/testgen-attempt`, `-stopped`, `-resumed` and `-activity-dropped`. Under them, each agent has its own
hand-written consumer:
- the chat's `sseParser`, `readChatStream`, `chatEvents`, `ChatStreamEvent` and `chatReducer`;
- the coverage screen's `runStream`.

On the server, each agent also has its own hand-built producer: `ChatTurnRunner` owns the channel and the order, and
`RunActivityProjection` builds events by hand. So an agent cannot move to another backend, and a screen cannot follow a
different agent, without both ends being rewritten. The goal is that agents and the web can each be swapped without
touching the other. Only the official protocol travels between them. The server uses the official
Microsoft Agent Framework hosting. The browser uses the official CopilotKit client.

## What Changes

- **BREAKING — only the protocol's events, from the official libraries.**
  - No `CUSTOM` event is emitted or consumed anywhere.
  - No AG-UI event is built by hand in application code. Events come from the official adapter and its documented
    mapping hooks only.
  - No application code parses SSE or calls an agent endpoint with `fetch`.
  - Architecture tests (.NET) and lint rules (web) fail the build if any of this comes back.
- **Every agent is a MAF `AIAgent` published with `MapAGUIServer`.** The package is
  `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`.
  - **BREAKING (packages):** `Microsoft.Agents.AI.*` moves 1.22 → 1.23. 1.22's hosting package is built against
    `AGUI.*` 0.0.6. 1.23 is built against the pinned 1.0.0.
- **The chat turn becomes an agent pipeline.** What `ChatTurnRunner` does around the model moves into a
  `DelegatingAIAgent` and function middleware:
  - principal and thread ownership, audit, the input and tool-result guardrails, the domain boundary;
  - the fee-adjustment confirmation, data cards and the account in focus;
  - the answer check, the turn's trace and its persistence.

  Cards, the confirmation interrupt and focus state are produced through the adapter's mapping hooks
  (`MapResult`, `MapInterrupt`, `MapResultAsStateSnapshot`). `ChatTurnRunner`, `RunRedaction`'s event rewriting,
  `RunFrameRecorder`'s custom-name handling and `AGUIStream`'s event factories are removed or reduced to what the
  hooks need.
- **Sources** travel as the search tool's own result. **BREAKING:** `maf-lab/sources` is removed. The tool-call result
  for a search carries the source references the client shows (doc id, section, path; never a snippet of free text
  beyond what is shown today). The client reads them by tool name.
- **The trace leaves the stream.** **BREAKING:** `maf-lab/trace` is removed. The monitor reads a running turn's trace
  from the trace API while the turn runs, and the stored trace as before. The run's own steps travel as
  `STEP_STARTED`/`STEP_FINISHED`, so the chat can still show progress from the stream.
- **The test-generation run becomes an AG-UI agent.** **BREAKING:** its four custom events are removed.
  - Attempts, the stop, the resume and the dropped notice become part of the run's `STATE_SNAPSHOT`/`STATE_DELTA`.
  - Phases stay `STEP_*`, and tool calls, text and reasoning stay the protocol's own.
  - A late subscriber rejoins with the protocol's `connect`, or, if the spike shows the server cannot serve it, with a
    new run that replays the run's events through the same adapter.
- **The web uses CopilotKit, headless, through CopilotKit's own runtime.**
  - A new wiring-only Node service, `copilot-runtime`, is reached at `/copilotkit/` behind the balancer. It registers
    the api's agents as `@ag-ui/client` `HttpAgent`s and forwards the user's bearer token.
  - The browser runs `@copilotkit/react-core` 1.76.0 headless with `runtimeUrl`.
  - The spike showed that the alternatives are CopilotKit's paid Enterprise tier (`selfManagedAgents`) or an API
    declared dev only.
  - `sseParser`, `readChatStream`, `chatEvents`, `ChatStreamEvent`, the stream half of `chatReducer`, `runStream` and
    `useRunEvents` are deleted.
  - Screens read messages, tool calls, state, steps and run status from `useAgent`. They add renderers only through
    `useRenderToolCall`, `useRenderActivityMessage` and `useHumanInTheLoop`, keyed by tool name or activity type.
  - Every renderer is optional; without one the generic rendering shows the item.
  - Our CSS and theme are kept, and progress for anything over 3 seconds is shown in the page's theme from the
    agent's `isRunning`, steps and state.
- **Rejoin and stop go through the protocol and the client.** Stop is the client's abort (`abortRun`), which ends the
  request and so the run on the replica serving it. The `POST /api/chat/{runId}/stop` and `GET /api/chat/{runId}`
  endpoints are replaced by what the official server offers. Whatever the spike finds missing is recorded in
  `design.md` as a gap, not filled with a private endpoint.
- **Proof that agents are swappable.** The same, unchanged web code runs:
  - the chat agent;
  - the test-generation agent;
  - a trivial echo `AIAgent` registered only in tests.

  Our server passes a conformance test driven by a generic `@ag-ui/client` `HttpAgent`.
- **Phase 0 is a feasibility spike** (running now in an isolated worktree). Its findings decide the open questions in
  `design.md` before any code moves.

No new Jev call is added and no Jev request changes. The existing guardrail and answer-check calls move unchanged from
`ChatTurnRunner` into the agent's middleware, so docs/rules/jev-usage.md is followed, not reopened.

Progress: no new CLI or make target. The UI's long-running processes (a chat turn, a test-generation run) keep themed
progress, now driven by the protocol's `isRunning`, `STEP_*` and state rather than by custom events.

## Capabilities

### New Capabilities

*(none — this narrows how existing capabilities reach the browser)*

### Modified Capabilities

- `agui-stream`: "What the protocol does not name travels as a custom event" is replaced by "Only the protocol's own
  events travel". Producing and consuming go through the official server and client only. Agents and screens can be
  swapped independently. Sources travel in the search tool's result. Stop and rejoin follow the official server and
  client.
- `turn-tracing`: "Live streaming of the trace" changes. The trace is no longer interleaved on the AG-UI stream; it is
  readable live through the trace API, and the run's steps travel as `STEP_*`. "The run's AG-UI frames are recorded"
  drops the custom-name and trace-by-reference parts.
- `chat-stream`: resume runs go through the same agent and the same client path, and no longer depend on trace events
  in the stream.
- `test-generation-runs`: "Progress to the browser over SSE" carries attempts, stop, resume and dropped notices as
  state instead of custom events, and is served by the official adapter.

`coverage-dashboard` and `web-ui` keep their requirements. Their screens already learn everything from the run's
AG-UI stream, and what changes under them (the shared CopilotKit client in place of per-screen readers, and the
monitor's live trace read from the trace API) is covered by `agui-stream` and `turn-tracing` and by design.md.

## Impact

- **Packages:**
  - .NET: `Microsoft.Agents.AI`, `.A2A` and `.Hosting.*` move 1.22 → 1.23. `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`
    1.23.0-preview.260928.1 is added.
  - .NET: `Microsoft.Extensions.AI.Abstractions` moves 10.10.0 → 10.10.1, which the hosting package requires.
  - web: `@copilotkit/react-core` 1.76.0 is added, and `@ag-ui/core` moves 1.0.0 → 1.0.1.
  - copilot-runtime: `@copilotkit/runtime` 1.76.0 and `@ag-ui/client` 1.0.1.
  - DECISIONS.md gets a new entry, and §26, §49 and §52 are marked as revised.
- **api:**
  - `Agent/ChatTurnRunner.cs` and `Agent/Streaming/*` are rewritten into the agent pipeline.
  - In `Endpoints/ChatEndpoints.cs`, `/api/chat` becomes `MapAGUIServer`.
  - `Coverage/RunActivityProjection.cs` and the `/api/coverage/runs/{id}/events` endpoint are replaced.
  - `Maf.Lab.Eval/Hosting/EvalAgentHost.cs` is affected too.
- **web:**
  - `web/src/chat/*` and `web/src/coverage/runStream.ts`, `useRunEvents.ts` and `RunActivity.tsx` change.
  - `web/src/monitor/*` (trace source), `web/src/api/types.ts` and the test helpers (`web/src/test/render.tsx`) and
    their tests change.
- **Evals and scripts:** `evals/ui-events.jsonl`, `scripts/capture_ui_events.sh`, `scripts/testgen_e2e.sh`.
- **Tests:** new architecture tests, the conformance test and the swap test. Stream tests across api and web are
  rewritten against the official client.
- **New service `copilot-runtime`** (Node 24, one replica, in compose and `make`, with `/health`), and a new nginx
  location `/copilotkit/`. The agent endpoints stay under `/api` on 7171.
- **Tenancy:** unchanged. The agent reads the principal from the request. No tenant parameter is added to any tool,
  endpoint or query builder.

## Documentation impact

- `docs/http-api.md`:
  - the AG-UI section: no `CUSTOM` table, sources in the search tool result, and stop and rejoin as the official
    server offers them;
  - the `/api/coverage/runs/{id}/events` row: state instead of custom events;
  - card activity types documented as data of `ACTIVITY_SNAPSHOT`.
- `docs/trace-events.md`: the trace is no longer streamed as `maf-lab/trace`, and the frame fields `name` and
  `traceSeq` go away.
- `README.md` (§ around line 366): the custom-event paragraph is replaced by the protocol-only rule and the CopilotKit
  client.
- `openspec/project.md`: the Frontend stack names CopilotKit (headless, via its runtime), and the Containers list and
  repository layout gain `copilot-runtime` (via `docs/docs-sync.toml` and `make docs`).
- `docs/http-api.md` also gains the `/copilotkit/` route. The `Maf.Lab.Api`
  description gets "AG-UI via MAF `MapAGUIServer`", edited through its `.csproj` `<Description>` and `make docs`.
- `CLAUDE.md`: the entry point list gains `copilot-runtime` x1 behind the balancer. A non-negotiable is added: "Only official AG-UI events: no `CUSTOM`, no hand-built events, no own SSE
  code; agents via `MapAGUIServer`, web via CopilotKit".
- `.github/copilot-instructions.md`: the same rule.
- `docs/telemetry.md`: no change, because the OTel instrumentation stays and the AG-UI server adds its own activity
  source.

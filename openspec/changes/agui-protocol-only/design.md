# Design

## Context

See proposal.md for why.

**Today.**
- `ChatTurnRunner` (1312 lines) runs a turn by hand:
  - It resolves the principal and the thread.
  - It screens the prompt with Jev, routes data intents (sometimes issuing a tool call without the model), refuses
    out-of-scope questions without a model call, and loads domain tools.
  - It invokes tools through its own middleware: audit, tool-result guard, confirmation, cards, focus.
  - It checks the answer with Jev, persists the turn, and records the trace and frames.
- It enumerates `AsChatResponseUpdatesAsync(...).AsAGUIEventStreamAsync(...)` into a channel it owns. It drops the
  adapter's run start and finish, adds its own events and `CUSTOM` events, and passes everything through
  `RunRedaction`.
- `RunActivityProjection` builds the test-generation stream by hand from database rows.
- The web has two hand-written readers (chat and run) over its own SSE parser.

**Packages.** `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` exposes `AddAGUIServer()` and
`MapAGUIServer(path, AIAgent | name | IHostedAgentBuilder)`. Its 1.23.0-preview.260928.1 is the first built against
`AGUI.*` 1.0.0, which we pin. `AGUI.Server` 1.0.0's `AGUIStreamOptions` offers the official extension points:
- `MapResult(tool, …)`, `MapResultAsStateSnapshot/Delta(tool)`;
- `MapCall(tool, …)`, `MapInterrupt(…)`, `MapContent(…)`;
- `MapStreamingToolCallArguments`, `WithUsageProvider`.

CopilotKit 1.76.0 runs on `@ag-ui/client`/`@ag-ui/core` 1.0.1 and has a headless entry.

**Spike (phase 0, 2026-10-01): GO.** It ran against a fake `IChatClient` with MAF 1.23.0, the hosting package
1.23.0-preview.260928.1, `AGUI.*` 1.0.0, MEAI 10.10.0 (Abstractions 10.10.1), CopilotKit 1.76.0 and
`@ag-ui/client` 1.0.1. Its findings are folded into the decisions below:
- Three setup items are required, and without them the official client rejects or breaks the stream:
  - a null-omitting JSON resolver;
  - the interrupt content types registered;
  - `rawEvent` stripped.
- `connect` is not supported. The server maps POST only, and `HttpAgent` throws `AGUIConnectNotImplementedError`,
  which CopilotKit swallows.
- CopilotKit OSS has no supported way to register an agent without its Node runtime. The only ways are
  `selfManagedAgents` (Enterprise tier, licence key) and `agents__unsafe_dev_only`/`addAgent__unsafe_dev_only`.

## Goals / Non-Goals

**Goals:**
- One shape for every agent: an `AIAgent` behind `MapAGUIServer`, with all AG-UI output coming from the adapter and
  the hooks registered in one place.
- One shape for every screen: CopilotKit's `useAgent`, reached through CopilotKit's own runtime, plus optional
  renderers keyed by tool name or activity type.
- Keep every existing guarantee: tenant from the principal, the redaction of free text, the guard and answer checks,
  persistence, trace, frames, the themed progress feedback.

**Non-Goals:**
- No CopilotCloud, no Intelligence tier, no GraphQL (v1) client, no CopilotKit styled UI kit, and no logic in the
  runtime beyond registering agents.
- No change to tools, MCP servers, A2A between api and agents, retrieval, Jev requests or the stored data model,
  beyond the additive fields named below.
- No change to the screens' layout or copy, except where a spec delta says so.

## Decisions

### D1. Packages
- `Microsoft.Agents.AI`, `.Abstractions`, `.A2A`, `.Hosting`, `.Hosting.A2A` and `.Workflows` move to 1.23 together.
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.23.0-preview.260928.1 is added. `AGUI.*` stays 1.0.0.
- Web:
  - `@copilotkit/react-core` 1.76.0 and `@ag-ui/client` 1.0.1 are added, and `@ag-ui/core` moves 1.0.0 → 1.0.1.
    These are exact versions, matching CopilotKit's own.
  - `zod` is added as CopilotKit's peer dependency.
- One DECISIONS.md entry covers all of it in the same commit.
- `Microsoft.Extensions.AI.Abstractions` moves 10.10.0 → 10.10.1, which the hosting package requires.
- *Rejected:* staying on 1.22. Its hosting package is compiled against `AGUI.*` 0.0.6. Forced to 1.0.0, it streams
  plain text but is an unsupported combination, and the spike did not test interrupts on it.
- `copilot-runtime` (D12) adds `@copilotkit/runtime` 1.76.0 and `@ag-ui/client` 1.0.1 on Node 24, at exact versions.

### D2. The chat agent is an `AIAgent` in front of the turn
*As built:* `ChatAgent : AIAgent` reads the run off the request (`TurnContents.Request`) and runs one of:
- a turn of `ChatTurnRunner`;
- an answer to a question (`ConfirmationService.ResumeAsync`);
- a rejoin (`RunRejoin`).

Each writes `ChatResponseUpdate`s to a channel the agent yields from. `ChatTurnRunner` keeps the turn's logic and builds
the MAF `ChatClientAgent` per turn (tools, history and focus are per turn), but it no longer touches the protocol: it
writes the model's updates, redacted, plus `TurnContents`. The turn id is the run id. A failed turn is kept, then the
agent throws, and the server ends the run with its own short `RUN_ERROR`. A mapped `RunErrorEvent` would be followed by
a `RUN_FINISHED`, so it is not used. The original plan follows.
- `ChatAgent : DelegatingAIAgent` wraps the existing `ChatClientAgent` (same model, same tools). Its
  `RunStreamingAsync` does what `RunAsync` of the runner did before the model call:
  - principal from `IPrincipalAccessor` (the request's), thread ownership, prompt screen;
  - Jev routing, domain loading and the trace start.
- It then either calls the inner agent, or yields the updates itself without a model call. That covers a refusal, an
  out-of-scope reply, and a routed tool call issued without asking the model, as `FunctionCallContent` and
  `FunctionResultContent`.
- After the stream ends, it runs the answer check and persists the turn.
- Tool middleware moves unchanged into function-invocation middleware on the inner agent: audit, tool-result guard,
  confirmation capture, card queue, focus change, unknown tool.
- *Why a delegating agent rather than the adapter's hooks:* §26 rejected "move everything behind the mapping hooks"
  for the right reason. The trace begins before the model is called, and the flow can block. A delegating agent is not
  a hook. It is the agent, so it can run before, around and after the model, and still leave every byte on the wire to
  the adapter.
- Thread: `RunAgentInput.threadId` is the conversation id. A missing one creates a conversation for the principal, an
  unknown well-formed one is claimed for the principal (protocol clients name their own threads), and another
  principal's thread is a 404 before the agent runs. `ChatRunFilter`, an endpoint filter on the mapped endpoint, does
  this and the message and run-id checks, so tenancy stays out of the agent's parameters.

### D3. What travels where (all through the adapter)

| Was | Becomes | Hook |
|---|---|---|
| `maf-lab/sources` | search tool's result content: `{ summary, sourceCount, sources: [{ docId, sectionPath, sourcePath, snippet }] }` (snippet as today) | `MapResult("search_documents" …)` |
| cards | `ACTIVITY_SNAPSHOT` (`activityType` unchanged, e.g. `maf-lab/holdings`) | `MapResult(tool, …)` for the three allow-listed tools |
| confirmation | `RUN_FINISHED` + generic interrupt (`InterruptRequestContent`), answered by `resume` (`InterruptResponseContent.Payload`) | the adapter maps it by itself once the interrupt types are registered (D11) |
| focus | `STATE_SNAPSHOT { focus }` at start and on change | `MapContent` of a state `DataContent` the agent yields first and after a focus change |
| turn progress | `STEP_STARTED/FINISHED` (screening, routing, `tool: <name>`, answer check) | `MapContent` of a step `DataContent` marker the agent yields |
| `maf-lab/trace` | not on the stream; trace API (D6) | — |
| `TOOL_CALL_ARGS` and `RESULT` redaction | identifier-only args, summary results | agent middleware (`AIAgentBuilder.Use(runStreamingFunc)`) rewrites `FunctionCallContent` arguments and `FunctionResultContent` before the adapter sees them |
| `rawEvent` | absent | a System.Text.Json contract modifier on the AG-UI JSON options (D11) |

- All hooks live in one class, `Agent/AGUI/AGUIMappings.cs`. That class is the only place in the solution allowed to
  construct a `BaseEvent`.
- `MapResult` only *adds* events after a result, so redaction cannot be a hook. It runs earlier, in agent middleware,
  so the adapter never sees the free text. `RunRedaction` becomes that middleware, and no stream filter exists.
- *Rejected for the confirmation:* MAF's `ApprovalRequiredAIFunction`. It maps to an interrupt automatically, but its
  resume needs a server-side `AgentSessionStore` (the in-memory one is dev only) plus a no-history provider. Its
  rejections surface only as a generic `RUN_ERROR`, and its resume payload must echo the tool call. Our
  `ConfirmationService` already keeps the proposal durably, signed and owned, which is what the spec's "answering
  twice" and "someone else's interrupt" rely on. A generic interrupt carries it unchanged.

### D4. Stop and rejoin
- **Stop** is the client's abort. Ending the request cancels the run on the replica that serves it, and the run's
  token already hangs off the request (§26). `POST /api/chat/{runId}/stop` and `RunStopper`'s cross-replica fan-out
  are deleted.
- **Rejoin.** The server does not implement `connect` (spike), so a rejoin is an ordinary run on the same thread
  with no new user message. The agent recognises it, finds the in-progress or ended
  run in `IRunStateStore`, and replays it as updates. The adapter turns them into events, and the run ends the way
  the original did.
- `GET /api/chat/{runId}` is deleted.
- **History.** The agent does not trust the history a client resends. It takes the new user message (or the interrupt
  answer) from `RunAgentInput` and builds the rest from the stored conversation, as the runner does today. The inner
  agent therefore runs with no history provider of its own, which also avoids the duplicate-key failure the spike
  saw with MAF's default in-memory history.

### D5. The test-generation run is an agent
- `TestGenRunAgent : AIAgent` is mapped at `/api/coverage/runs/agent`. The thread is `testgen:<run id>`, and any
  signed-in user may read it.
- Its `RunStreamingAsync` does what the `/events` endpoint does today: poll `RunActivityStore` every `EventPollEvery`.
  The difference is that it yields MEAI content instead of building events:
  - text and reasoning content;
  - function call and result contents for tool activity;
  - step contents for phases;
  - state content for the summary, attempts, stop, resumes and dropped notice.
- `RunActivityProjection` keeps its cursor logic and loses every event constructor.
- Terminal: the run's result, or an error carrying the reason as its code.
- A late subscriber is a new run on the same thread, which replays the run first (D4's rejoin, for free).

### D6. The live trace leaves the stream
- `GET /api/runs/{runId}/trace?after={seq}` returns the trace events recorded after `seq` and whether the run is over.
  The client chooses the run id (it is in `RunAgentInput`), so the monitor knows it before the first event.
- The access rules are those of `/api/turns/{turnId}/trace`. A run with no turn (a resume) is readable by the thread's
  owner while it is kept in `IRunStateStore`.
- The monitor polls it every 500 ms while `isRunning`, then switches to the stored trace.
- *Rejected:* a second SSE stream. It would be our own stream code, which this change forbids.
- *Rejected:* leaving the trace as `STEP` events only, because the monitor needs the full trace event data.

### D7. Frames
- The frame recorder taps the adapter's output, not our channel (which no longer exists).
- `RunTap` (middleware, `Agent/AGUI/RunTap.cs`) tees the response body on `/api/chat` and decodes it with
  `System.Net.ServerSentEvents.SseParser`, the platform's decoder.
  - It feeds the raw JSON of each event to `RunFrameRecorder` (frames) and `RunStateTracker` (the rejoin snapshot).
  - It reads and never writes, and it is the one file the arch test lets read the event stream.
  - The official server leaves the SSE `event:` field empty, so an event's type is read from its payload.
- `name` and `traceSeq` leave the frame.

### D8. Web: CopilotKit headless, through the runtime
- `AgentsProvider` creates one `CopilotKitCoreReact({ runtimeUrl: '/copilotkit', headers })` and provides it through
  `CopilotKitContext` (from `@copilotkit/react-core/v2/context`):
  - headers come from `useAuth` and are refreshed on token change;
  - the agents are `chat` and `testgen`. A run is the `testgen` agent on thread `testgen:<id>`, and every view of one
    run shares that agent and thread (the existing "one stream per run" rule).
- *Why not `CopilotKitProvider`:* the `/v2` index entry imports Tailwind CSS and is the 16.5 MB build.
- *Rejected after the spike:*
  - `selfManagedAgents` is CopilotKit's Enterprise tier and needs a licence key.
  - `agents__unsafe_dev_only` is declared dev only.
  - `@ag-ui/client` alone would not be CopilotKit.

  Decided by the user on 2026-10-01: CopilotKit's own runtime, D12.
- The chat screen and the Activity modal use `useAgent`:
  - messages, `isRunning` and `state`;
  - tool calls from the messages;
  - steps from the agent's event subscription.
- Renderers:
  - cards from the agent's activity messages, by `activityType` (`useRenderActivityMessage` is not in the headless
    entry, so a small switch over `activityType` renders them; an unknown type is ignored);
  - sources via `useRenderToolCall('search_documents')`;
  - the confirmation via `useHumanInTheLoop` on the interrupt;
  - run attempts and notices are rendered from `state`.
- Every other tool call or activity gets a generic card.
- Styling: our CSS modules and theme tokens. The headless and context entries import no CSS and work unstyled
  (spike). The `/v2` index entry imports a 90 KB Tailwind build and KaTeX fonts, so it is never imported.
- Progress: one `RunProgress` component in the page theme reads `isRunning` and the current step, and is used for
  any agent.
- History: opening a conversation loads `GET /api/conversations/{id}` (REST, unchanged) into the agent's messages.
  Pending confirmations come from `GET …/pending` as today.
- Deleted:
  - `sseParser.ts`, `readChatStream.ts`, `chatEvents.ts`;
  - `ChatStreamEvent` and its types;
  - the stream actions of `chatReducer` (it keeps UI-only state);
  - `runStream.ts`, `useRunEvents.ts`.

### D9. Guards that keep it this way
- **.NET arch test** (xUnit, reflection plus a source scan of `src/`):
  - no reference to `CustomEvent`;
  - the `AGUI.Abstractions`/`AGUI.Server` namespaces only under `src/Maf.Lab.Api/Agent/AGUI/`, the agents' wiring to
    the official server (this also catches target-typed `new()` events, which a construction scan cannot see);
  - within it, `new …Event` only in `AGUIMappings.cs`;
  - no `text/event-stream`, `ServerSentEvents` or `SseItem` in the api, so every AG-UI endpoint is `MapAGUIServer`'s.
- **Web ESLint** `no-restricted-syntax` and `no-restricted-imports`:
  - `EventType.CUSTOM`;
  - importing `@ag-ui/core` event classes for construction outside tests;
  - the string `text/event-stream`;
  - `fetch(` to `/api/chat` or `/api/coverage/runs/agent`.
- Both run in `make lint`, `make test` and CI.

### D10. Proving swappability
- **`EchoAgent`** (tests only) is mapped at `/api/test/echo` in the integration host. A web test renders the chat
  screen with `agentId` pointed at an `HttpAgent` for it, through a test server, and asserts the echo, generic tool
  card and terminal state. This is the web half.
- **A conformance test** in Vitest (node) drives our real chat and testgen endpoints with a bare `HttpAgent`:
  - start, follow to terminal;
  - interrupt then resume;
  - abort;
  - assert no `CUSTOM` and a single terminal event.

  It runs against the stack in `make verify`.

### D11. Server setup the official client needs (spike)
These are all in `Agent/AGUI/AGUIHosting.cs`:
- **Null-omitting JSON:** `PostConfigure<JsonOptions>` inserts `AGUIJsonUtilities.DefaultTypeInfoResolver` first.
  Without it, `HttpAgent` rejects `RUN_STARTED` (`parentRunId`, `input`, `subagentRunId` sent as `null`).
- **`AGUIJsonUtilities.RegisterInterruptContentTypes`** on the same options. The hosting package does not call it, and
  without it any interrupt ends in `RUN_ERROR` (to be reported upstream).
- **A contract modifier** that stops `rawEvent` from serializing on every `BaseEvent`.
- **`MapAGUIServer(...).RequireAuthorization()`**. The principal (`firm_id`) is read through `IHttpContextAccessor`
  inside the agent and its tools, as everywhere else.
- **Stream options** per endpoint via `.WithMetadata(AGUIMappings.For…())`, which wins over DI, so each agent's
  mappings stay with its endpoint.

### D12. `copilot-runtime`: CopilotKit's runtime, wiring only
- **Where it lives:** a Node 24 service in `copilot-runtime/`: `package.json`, `server.ts` and a `Dockerfile`.
- **What it runs:** `createCopilotNodeListener` with `new CopilotRuntime({ agents: ({ request }) => ({ chat, testgen }) })`.
  - Each agent is an `HttpAgent` pointed at the api's `MapAGUIServer` endpoint through the balancer (`http://lb/...`),
    created per request.
  - The request's `Authorization` header is forwarded, so the api still resolves the principal and the tenant itself.
    The runtime never reads the token.
- **What it must not do:**
  - add tools, prompts, middleware, memory or logging of content;
  - run as an Intelligence runtime or send telemetry (`SCARF_ANALYTICS=false`, CopilotKit telemetry off).
- **Routing:** nginx routes `/copilotkit/` to it, and nothing else reaches it. It runs as one replica. Its in-memory
  agent runner only buffers a live run for a reconnecting browser. What a run is, and its rejoin, stay in the api
  (D4), so a runtime restart loses nothing durable.
- **Stop:** the browser's abort ends the runtime's request, which aborts its `HttpAgent` request to the api, which
  cancels the run (D4). A test proves the chain.
- **Health:** `/health`, in `make` and `make doctor`, like every other service.

### Jev
No Jev request is added, removed or changed. The prompt screen, routing, tool-result guard, relevance judge and answer
check keep their state fields, questions, thresholds, fallbacks and the pinned `jev-1.13.0`. Only the class that calls
them changes (D2). Their eval suites must stay at baseline.

## Risks / Trade-offs

- **The hosting package is a preview, with two bugs found by the spike** (null fields, interrupt types). → Pinned
  exactly, with the reason in DECISIONS. Both workarounds are in D11 with a test each, so an upgrade that fixes or
  breaks them is noticed. Both are reported upstream.
- **Free text leaking through the adapter.** → Redaction runs before the adapter (D3). A test asserts that no
  `TOOL_CALL_ARGS` or `TOOL_CALL_RESULT` carries a query, a reason or document text (the existing redaction tests move
  over unchanged).
- **The card must follow its tool result.** The adapter's ordering differs from the runner's. → The activity is
  emitted by `MapResult` for the same call, so it is adjacent by construction. The existing card ordering test stays.
- **CopilotKit's weight** (spike, minified and gzipped, on top of React's 68 KB):
  - `HttpAgent` alone adds 187 KB;
  - the headless core and hooks add 255 KB;
  - the full `/v2` provider adds about 593 KB initial (16.5 MB JS in 466 chunks).

  It also pulls `@scarf/scarf` (an install-time telemetry script). → Headless only, the chat and coverage routes
  lazy-loaded, and `SCARF_ANALYTICS=false` set in the web build.
- **Polling the live trace.** → 500 ms, only while the monitor is open and the run is live. It is one indexed query
  on `seq`.
- **A Node hop in front of every agent.** It adds one service and one hop. → The service is wiring only (D12), is
  covered by the conformance test through it, and is a single file anyone can read. An agent stays reachable directly
  by any AG-UI client, and the conformance test also drives the api directly.
- **Losing behaviour while rewriting a 1312-line class.** → The existing `ChatTurnRunner` tests are ported first,
  against the agent through the in-memory `MapAGUIServer` host, and must pass before the runner is deleted.
- **Cross-replica stop is lost.** → It only existed for an explicit stop call reaching the wrong replica. Abort on the
  request is what the browser does, and it always reaches the right replica.

## Migration Plan

One branch, `agui-protocol-only`, merged to `main` when the whole change is green. Old and new paths are never shipped
side by side, because two wire contracts is exactly what this removes.

1. **Spike** (phase 0, done). Its findings are recorded in Context and D1–D11.
2. Packages and the guard tests, with the current violations listed as the expected failures.
3. Server chat: `ChatAgent`, `AGUIMappings`, `MapAGUIServer`, live trace API, frames. The ported runner tests turn
   green, and the runner is deleted.
4. Server test-generation run as an agent.
5. Web: provider, chat and monitor, then Activity modal and tree badges. Old readers are deleted.
6. Evals, scripts, docs (`make docs`, `make docs-check`), the conformance and swap tests, and `make ci` green.

Rollback: do not merge the branch. Nothing is migrated in the data, because the stored turns, traces and frames keep
their shape (`aguiFrames` simply stops containing `CUSTOM`).

## Open Questions

- Whether CopilotKit's generic tool-call rendering is usable as-is in our theme, or needs our own generic card. Either
  way it is one component in D8.

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

CopilotKit 1.76.0 runs on `@ag-ui/client`/`@ag-ui/core` 1.0.1. It accepts `selfManagedAgents: Record<string,
AbstractAgent>` and has a headless entry.

The phase-0 spike checks the assumptions marked **[spike]** below. Each has its fallback decided here, so the spike's
result picks a branch, not a new design.

## Goals / Non-Goals

**Goals:**
- One shape for every agent: an `AIAgent` behind `MapAGUIServer`, with all AG-UI output coming from the adapter and
  the hooks registered in one place.
- One shape for every screen: CopilotKit's `useAgent` over an `HttpAgent`, plus optional renderers keyed by tool
  name or activity type.
- Keep every existing guarantee: tenant from the principal, the redaction of free text, the guard and answer checks,
  persistence, trace, frames, the themed progress feedback.

**Non-Goals:**
- No CopilotKit Node runtime, CopilotCloud, GraphQL client or CopilotKit's styled UI kit.
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
- *Rejected:* staying on 1.22. Its hosting package is compiled against `AGUI.*` 0.0.6, and binding it to 1.0.0 is
  unsupported **[spike checks whether it even loads]**.
- *Rejected:* a Node `CopilotRuntime` service. It is a second backend and a GraphQL hop, and it would put a
  non-.NET service in front of every agent.

### D2. The chat agent is a `DelegatingAIAgent` around the MAF chat agent
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
- Thread: `RunAgentInput.threadId` is the conversation id. A missing one creates a conversation for the principal, and
  another principal's thread is a 404 before the agent runs. This is a filter on the mapped endpoint, so tenancy stays
  out of the agent's parameters.

### D3. What travels where (all through the adapter)

| Was | Becomes | Hook |
|---|---|---|
| `maf-lab/sources` | search tool's result content: `{ summary, sourceCount, sources: [{ docId, sectionPath, sourcePath, snippet }] }` (snippet as today) | `MapResult("search_documents" …)` |
| cards | `ACTIVITY_SNAPSHOT` (`activityType` unchanged, e.g. `maf-lab/holdings`) | `MapResult(tool, …)` for the three allow-listed tools |
| confirmation | `RUN_FINISHED` + interrupt, answered by `resume` | `MapInterrupt` on the proposal content **[spike: MAF approval content may map by itself]** |
| focus | `STATE_SNAPSHOT { focus }` at start and on change | `MapResultAsStateSnapshot` for reads that move focus; the start snapshot is emitted by `MapContent` of a state content the agent yields first **[spike]** |
| turn progress | `STEP_STARTED/FINISHED` (screening, routing, `tool: <name>`, answer check) | `MapContent` of a step content the agent yields |
| `maf-lab/trace` | not on the stream; trace API (D6) | — |
| `TOOL_CALL_ARGS` and `RESULT` redaction | identifier-only args, summary results | `MapCall` / `MapResult` per tool; one default for all other tools **[spike: whether a mapping replaces or adds]** |
| `rawEvent` | absent | adapter option **[spike]** |

- All hooks live in one class, `Agent/AGUI/AGUIMappings.cs`. That class is the only place in the solution allowed to
  construct a `BaseEvent`.
- **Fallback if a hook adds instead of replaces, or `rawEvent` cannot be switched off:** a stream filter on the
  endpoint (`RunRedaction`, kept) may *remove fields* from adapter events. It can never create, reorder or retype an
  event.

### D4. Stop and rejoin
- **Stop** is the client's abort. Ending the request cancels the run on the replica that serves it, and the run's
  token already hangs off the request (§26). `POST /api/chat/{runId}/stop` and `RunStopper`'s cross-replica fan-out
  are deleted.
- **Rejoin.** If the server implements the protocol's `connect`, that is used **[spike]**. Otherwise a rejoin is an
  ordinary run on the same thread with no new user message. The agent recognises it, finds the in-progress or ended
  run in `IRunStateStore`, and replays it as updates. The adapter turns them into events, and the run ends the way
  the original did.
- `GET /api/chat/{runId}` is deleted in both cases.

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
- It reads the server's `AGUIServerInstrumentation` activity events if they carry each event **[spike]**. Otherwise it
  tees the response body on agent routes and decodes it with `System.Net.ServerSentEvents.SseParser` (the platform's
  decoder) and `AGUI.Abstractions` types.
- `name` and `traceSeq` leave the frame.

### D8. Web: CopilotKit headless, one registry of agents
- `AgentsProvider` wraps the app in `CopilotKitProvider` (headless) with `selfManagedAgents`:
  - `chat` → `new HttpAgent({ url: '/api/chat', headers })`;
  - `testgen:<id>` agents, created on first use and kept while subscribed, so every view of a run shares one stream
    (the existing "one stream per run" rule);
  - headers come from `useAuth` and are refreshed on token change.
- The chat screen and the Activity modal use `useAgent`:
  - messages, `isRunning` and `state`;
  - tool calls from the messages;
  - steps from the agent's event subscription.
- Renderers:
  - cards via `useRenderActivityMessage(activityType)`;
  - sources via `useRenderToolCall('search_documents')`;
  - the confirmation via `useHumanInTheLoop` on the interrupt;
  - run attempts and notices are rendered from `state`.
- Every other tool call or activity gets a generic card.
- Styling: our CSS modules and theme tokens. No Tailwind and no CopilotKit stylesheet is imported **[spike: headless
  works unstyled]**.
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

### Jev
No Jev request is added, removed or changed. The prompt screen, routing, tool-result guard, relevance judge and answer
check keep their state fields, questions, thresholds, fallbacks and the pinned `jev-1.13.0`. Only the class that calls
them changes (D2). Their eval suites must stay at baseline.

## Risks / Trade-offs

- **The hosting package is a preview.** → Pinned exactly, with the reason in DECISIONS. The spike runs the full
  interrupt, resume and abort path before any code moves. The A2A hosting is already on a preview of the same train.
- **A hook adds events instead of replacing them, leaking free text.** → D3's field-removing filter. A test asserts
  that no `TOOL_CALL_ARGS` or `TOOL_CALL_RESULT` carries a query, a reason or document text (the existing redaction
  tests move over unchanged).
- **The card must follow its tool result.** The adapter's ordering differs from the runner's. → The activity is
  emitted by `MapResult` for the same call, so it is adjacent by construction. The existing card ordering test stays.
- **CopilotKit's weight.** It pulls `rxjs`, `@ag-ui/client`, markdown and UI dependencies even headless. → The
  headless entry and tree-shaking are measured in the spike. The budget is under +250 KB gzip on the chat route;
  above it, `@ag-ui/client` alone is used through a thin `useAgent`-shaped hook, which is still the official client.
- **Polling the live trace.** → 500 ms, only while the monitor is open and the run is live. It is one indexed query
  on `seq`.
- **Losing behaviour while rewriting a 1312-line class.** → The existing `ChatTurnRunner` tests are ported first,
  against the agent through the in-memory `MapAGUIServer` host, and must pass before the runner is deleted.
- **Cross-replica stop is lost.** → It only existed for an explicit stop call reaching the wrong replica. Abort on the
  request is what the browser does, and it always reaches the right replica.

## Migration Plan

One branch, `agui-protocol-only`, merged to `main` when the whole change is green. Old and new paths are never shipped
side by side, because two wire contracts is exactly what this removes.

1. **Spike** (phase 0). Its findings are recorded in this design. Each **[spike]** gets its branch chosen.
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

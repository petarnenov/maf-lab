# Tasks

## 0. Feasibility spike

- [x] 0.1 Run the spike in an isolated worktree (MAF 1.23 `MapAGUIServer` + `AGUIStreamOptions` hooks, interrupt → resume, `rawEvent`, `connect`, auth principal, CopilotKit `selfManagedAgents` + `HttpAgent`, headless unstyled, bundle size). Verify: a report marks each **[spike]** item in design.md WORKS / FAILS / PARTIAL with evidence
- [x] 0.2 Record the findings in design.md: choose the branch of every **[spike]** decision and remove the marker (the CopilotKit connection decided by the user: its own runtime, D12). Verify: `grep -c "\[spike" design.md` is 0 and `openspec validate agui-protocol-only --strict` passes

## 1. Packages and guards

- [ ] 1.1 Move `Microsoft.Agents.AI.*` to 1.23 and `Microsoft.Extensions.AI.Abstractions` to 10.10.1, add `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.23.0-preview.260928.1, and write the DECISIONS.md entry (revising §26, §49, §52) in the same commit. Verify: `dotnet build` and `make test` pass, and `dotnet list package --include-transitive` shows `AGUI.*` 1.0.0
- [ ] 1.2 Add `@copilotkit/react-core` 1.76.0 and `zod` to the web, and move `@ag-ui/core` to 1.0.1 (exact versions, `SCARF_ANALYTICS=false` in the web image; DECISIONS in the same commit). Verify: `make build-web` passes
- [x] 1.3 Add the .NET architecture test (D9): no `CustomEvent`, AG-UI types only under `Agent/AGUI/`, events built only in `AGUIMappings.cs`, no own event stream. Verify: the test fails today, listing `AGUIStream.cs`, `RunActivityProjection.cs`, `ChatTurnRunner.cs` and the two endpoint files among the expected violations
- [x] 1.4 Add the web ESLint restrictions (D9). Verify: `make lint` reports today's violations in `chatEvents.ts`, `readChatStream.ts`, `useChatStream.ts` and `runStream.ts` (importers of `sseParser`), and nothing else

## 2. Chat agent on the official server

- [ ] 2.0 Add `Agent/AGUI/AGUIHosting.cs` (D11): null-omitting resolver, interrupt content types, `rawEvent` modifier, and the `RequireAuthorization` mapping helper. Verify: a test per item (no `null` optional fields on `RUN_STARTED`, a generic interrupt serializes, no `rawEvent` on the wire, 401 without a token)

- [ ] 2.1 Port the `ChatTurnRunner` tests to drive the agent through an in-memory `MapAGUIServer` host (stream assertions on protocol events). Verify: they compile and fail only for the missing agent
- [ ] 2.2 Create `ChatAgent : DelegatingAIAgent` with the pre-model steps (principal, thread ownership filter, prompt screen, Jev routing, domain loading, refusal and out-of-scope replies without a model call, routed tool call). Verify: the ported tests for refusal, out-of-scope, routing and forced search pass
- [ ] 2.3 Move tool middleware (audit, tool-result guard, confirmation capture, card queue, focus change, unknown tool) to function-invocation middleware on the inner agent. Verify: the ported guard, audit and unknown-tool tests pass
- [ ] 2.4 Write `AGUIMappings` (D3): search result with sources, three cards as `ACTIVITY_SNAPSHOT`, confirmation interrupt, focus state, steps, default identifier-only args and summary results. Verify: the card ordering, interrupt, focus and redaction tests pass, and a test asserts no args or result carries query, reason or document text
- [ ] 2.5 Run the answer check and persistence after the stream. Verify: the answer-check and history tests pass, and a reopened conversation shows the same turns and cards
- [ ] 2.6 Map `/api/chat` with `MapAGUIServer`. Delete `ChatTurnRunner`, `AGUIStream`'s event factories, `POST /api/chat/{runId}/stop` and `GET /api/chat/{runId}`. Implement rejoin per D4. Verify: the stop, rejoin, ownership and "run begins and ends once" tests pass, and the arch test no longer lists chat files
- [ ] 2.7 Add `GET /api/runs/{runId}/trace?after=` (D6), with the access rules of the turn trace. Verify: tests for owner, other user (404), resume run, and incremental `after`
- [ ] 2.8 Re-home the frame recorder on the adapter output (D7) and drop `name` and `traceSeq`. Verify: the frame tests (every event recorded, cap, no payload in logs) pass
- [ ] 2.9 Move `Maf.Lab.Eval/Hosting/EvalAgentHost.cs` to the new agent. Verify: `make eval SUITE=selection` and the guard, answer-check and confirmation suites match the accepted baseline

## 3. Test-generation run as an agent

- [ ] 3.1 Create `TestGenRunAgent` (D5) yielding content from `RunActivityStore`: steps, tool calls, text, reasoning and state with summary, `attempts`, `stop`, `resumes` and `dropped`. Map it at `/api/coverage/runs/agent`. Verify: the scenarios of the modified "Progress to the browser over SSE" pass, including the late subscriber, restart, budget stop and a replay of a pre-scope run
- [ ] 3.2 Delete the `/api/coverage/runs/{id}/events` endpoint and `RunActivityProjection`'s event constructors. Verify: the arch test is fully green

## 4. Web on CopilotKit

- [ ] 4.0 Create `copilot-runtime/` (D12): `CopilotRuntime` with a per-request agents factory (`chat`, `testgen`) as `HttpAgent`s to the api through the balancer, forwarding `Authorization`, `/health`, telemetry off, a Dockerfile, a compose service (one replica), an nginx `/copilotkit/` location, and `make`/`make doctor` health. Verify: `make` brings it up healthy, and through `http://localhost:7171/copilotkit/` a Node `HttpAgent` script runs a chat turn to `RUN_FINISHED` with the user's tenant, and gets 401 without a token
- [ ] 4.0a Prove stop through the runtime. Verify: aborting the browser-side run cancels the api run within one second (the api reports cancelled, and no tool runs afterwards)

- [ ] 4.1 Add `AgentsProvider` (`CopilotKitCoreReact` with `runtimeUrl: '/copilotkit'` via `CopilotKitContext`, auth headers refreshed on token change, the `testgen` agent shared per `testgen:<id>` thread). Verify: a unit test shows that two subscribers of one run open one request, and that a token change updates headers
- [ ] 4.2 Add a themed `RunProgress` and a generic tool/activity card. Verify: a test shows progress during a run longer than 3 s, in both themes, and an unknown tool renders generically
- [ ] 4.3 Move the chat screen to `useAgent`: messages, `isRunning`, cards by `activityType` from the agent's activity messages, sources via `useRenderToolCall('search_documents')`, the confirmation via `useHumanInTheLoop`, focus from `state`, history loaded into messages. Verify: the `ChatPage.*` tests (cards, focus, confirmation, reasoning, errors) pass on the new path
- [ ] 4.4 Make the monitor read the live trace from `/api/runs/{runId}/trace` while running and the stored trace afterwards. Keep time travel. Verify: the monitor and time-travel tests pass, and a resume run shows its trace
- [ ] 4.5 Move the Activity modal, the file status and tree badges to `useAgent` on `testgen:<id>`, with attempts, stop and resume notices from `state`. Verify: the `RunActivity`, `TreeRunBadge` and live-status tests pass
- [ ] 4.6 Delete `sseParser`, `readChatStream`, `chatEvents`, `ChatStreamEvent`, the stream half of `chatReducer`, `runStream` and `useRunEvents`. Verify: `make lint` has no AG-UI restriction violations, and `make build-web` and `make test-web` pass

## 5. Proof of swappability

- [ ] 5.1 Add the test-only `EchoAgent` and a web test that runs the unchanged chat screen against it. Verify: the echo answer, the generic tool card and the finished state are shown
- [ ] 5.2 Add the conformance test that drives the chat and testgen agents with a bare `HttpAgent`, both directly on the api and through `/copilotkit/`: start, interrupt, resume, abort, no `CUSTOM`, one terminal event. Wire it into `make verify`. Verify: it passes against `make` on http://localhost:7171

## 6. Jev, evals and end-to-end

- [ ] 6.1 Run the Jev review checklist (docs/rules/jev-usage.md §7) on the moved call sites, and test on labeled inputs including Bulgarian ones (prompt screen, routing, tool-result guard, answer check). Verify: the checklist outcome is recorded in DECISIONS.md, and the labeled cases give the same decisions as on `main`
- [ ] 6.2 Update `evals/ui-events.jsonl`, `scripts/capture_ui_events.sh` and `scripts/testgen_e2e.sh` to protocol-only events. Verify: re-capture `evals/ui-events.jsonl` from the stack and check that no frame is `CUSTOM`, and `scripts/testgen_e2e.sh` passes
- [ ] 6.3 Run the full stack: chat with a card, a confirmation approve and reject, a stop, a reload during a run, and a test-generation run watched from two views. Verify: `make ci` is green and the manual walk-through matches the specs

## 7. Documentation

- [ ] 7.1 Update `docs/http-api.md`: the AG-UI section without `CUSTOM`, sources in the search result, stop and rejoin, the testgen agent, the live trace endpoint, and card activity types as data. Verify: `make docs-check` passes for routes
- [ ] 7.2 Update `docs/trace-events.md`: the trace is no longer streamed, the live endpoint, and the frame fields. Verify: by review against D6 and D7
- [ ] 7.3 Update `README.md` (the custom-event paragraph), `CLAUDE.md` (the protocol-only non-negotiable) and `.github/copilot-instructions.md`. Verify: `grep -rn "maf-lab/trace\|maf-lab/sources\|testgen-attempt" README.md CLAUDE.md docs .github` returns nothing
- [ ] 7.4 Update `openspec/project.md` (frontend stack, containers), `docs/docs-sync.toml` (layout entry for `copilot-runtime/`), `CLAUDE.md` (entry point list) and the `Maf.Lab.Api` `.csproj` `<Description>`, then run `make docs` (never editing a `generated:` block by hand). Verify: `make docs-check` passes

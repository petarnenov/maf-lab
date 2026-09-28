# Tasks

Depends on `add-jev-passage-relevance` task 1.1 (the Jev plumbing and `JevOptions` live in `Maf.Lab.Retrieval.Jev`).

## 1. Routing questions and rule

- [x] 1.1 Add `RouteDataTools` and `MinRouteProbability` (0.8) to `JevOptions`. With routing on, add the three tool
      Nouls (structured instructions `{tool, question}`) and the `run_status` Choice to the intent request; with it off
      the request is byte-for-byte today's. Verify unit tests with `FakeJev`: one request, state holds only
      `user_question`, question ids and types as designed, and neither the question nor the key in any instruction.
- [x] 1.2 Add `ToolRoute` (tool, arguments, probability) and a route reason to `IntentDecision`; implement the rule of
      design D2 with a small argument extractor (run ids; month-year periods in English, Bulgarian Cyrillic and Latin;
      unparsed time expressions). Verify unit tests for: status of one run, runs by status, June 2026, "last month"
      not routed, two run ids not routed, write veto, below floor, non-data intent, missing answers, routing off.

## 2. Issuing the routed call

- [x] 2.1 `RequiredToolModeChatClient` issues the routed call when the required tool is the routed one;
      `ChatTurnRunner` sets `RequireSpecific(route.Tool)` for a routed turn, installs the wrapper when a route exists,
      and traces the routing answer on the intent event and the reason on `tool.forced`. Verify through `ApiFactory`
      that a routed status question calls `get_billing_run_status` before any model call and makes one model call;
      that an unrouted data question makes the model choose; that a write is never issued on the model's behalf.

## 3. Test doubles

- [x] 3.1 `FakeJev` answers the routing questions (settable per-tool probabilities and status; defaults from keywords);
      `compose/ollama-stub/server.py` answers them from keywords so the model-free e2e stays deterministic. Verify
      `make test` and a local request to the stub.

## 4. Measure and decide

- [x] 4.1 Run `make eval-intent` with routing on and confirm the intent metrics stay at the baseline.
- [x] 4.2 Run `make eval SUITE=selection` ≥ 3 times with routing on and ≥ 1 with it off, keeping the eval databases;
      measure from the traces the model calls and latency of routed data turns vs. the same turns unrouted.
- [x] 4.3 Apply the rule of design D5, set the default, and record probe, runs, latency, timeouts and the decision in
      `DECISIONS.md`.

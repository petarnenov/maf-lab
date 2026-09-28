# Tasks

## 1. Jev screening

- [x] 1.1 Add `JevGuardQuestions` (prompt and content sets, structured instructions + criteria, `guard_` ids) and `JevGuard` (standalone prompt/content screening on the `"jev"` client, bounded by `Guard:TimeoutSeconds`, fail → unscreened with a reason); verify by unit tests that the key is only in the Authorization header and the text only in the state
- [x] 1.2 Ask the prompt questions in `JevIntentClassifier`'s one request and carry the probabilities on `IntentDecision` on every path that got an answer (including low confidence and off-domain); verify by unit tests (one request, screening kept when the intent is unused)
- [x] 1.3 Add `GuardOptions` (`Guard` section: Enabled, PromptBlockAt 0.65, ContentWithholdAt 0.85, TimeoutSeconds 2, MaxConcurrent) and the `Guardrail` policy (prompt verdict, refusal text EN/BG, per-item tool-result withholding with a neutral notice, consultation judging, trace event, structure-only log line); verify by unit tests

## 2. Chat turn

- [x] 2.1 Refuse a blocked prompt in `ChatTurnRunner` without a model call or tool, streaming the fixed refusal through the AG-UI adapter, keeping it out of model history; verify by an API test (no chat request, refusal answer, `guardrail_blocked`, no history message)
- [x] 2.2 Screen every non-error tool result in the tool middleware before sources and envelope; withhold flagged items; verify by an API test (poisoned excerpt withheld, not a source, clean excerpt delivered, `guardrail_withheld`)
- [x] 2.3 Fail open for prompt and tool results when Jev fails or times out; verify by API tests with a hanging and a failing FakeJev (turn answers, trace says unscreened)
- [x] 2.4 Add signals `guardrail_blocked`/`guardrail_withheld` and trace kind `guardrail`, derived from the turn's guard events; verify in the trace test

## 3. Other agents

- [x] 3.1 Screen the consultation result in `FeeAdjustmentFlow`: flagged → failed review; unscreened refusal/question → words withheld, outcome stands; verify by tests with the hostile reviewer (ia-01 flagged → nothing asked or written) and with Jev down (refusal still refused, reason not given to the model)
- [x] 3.2 Screen a partner's question in `AssistantBridge` and add tool middleware there that screens and envelopes; verify by an A2A test (injected question refused without a model call)

## 4. Stand-ins, web, docs

- [x] 4.1 `FakeJev`: answer `guard_*` Nouls from a settable function (default 0) and read `user_question` or `untrusted_text`; CI stub: answer them from unmistakable phrases; verify `make test` and a stub request by hand
- [x] 4.2 Web: trace kind union and colour, signal labels; verify `make lint` and web tests
- [x] 4.3 `docs/trace-events.md`: the `guardrail` event; DECISIONS.md §34 with the measurements, thresholds and failure modes

## 5. Evals

- [x] 5.1 Add `evals/guardrail.jsonl` (377 labelled texts — 356 at design time, 21 held-out cases added after the thresholds were fixed), dataset loader and `GuardrailSuite` (no chat model, no tools; detection and benignPass per side/language/category/split, unscreened count, named failures), thresholds in `eval.json`, `make eval-guardrail`; verify two runs
- [x] 5.2 Accept the guardrail baseline; run `make eval SUITE=injection` and `SUITE=selection` and compare with their baselines

## 6. Verification

- [x] 6.1 `make lint`, `make test`, `openspec validate add-jev-guardrail --strict`, `make specs` all pass

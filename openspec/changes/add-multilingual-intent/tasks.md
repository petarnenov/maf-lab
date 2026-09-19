# Tasks

## 1. Classifier

- [x] 1.1 Turn `IntentClassifier` into an injectable `IIntentClassifier` that keeps the static rules as stage one and returns the decision (intent, stage, raw answer, duration, reason) instead of a bare enum; keep the existing pure `Classify` for the rules; verify the current rule tests still pass unchanged and a new test asserts an English procedural question never reaches stage two
- [x] 1.2 Add the model stage: client from `IChatClientFactory.CreateChatClient(Agent:IntentModel)` (empty → chat model), non-streaming, temperature 0, small `MaxOutputTokens`, labels-only system message, question wrapped as data; parse by trimming/upper-casing/stripping punctuation and matching the five intents; verify tests with a scripted model for a Bulgarian procedural question (→ Procedural, forced), a Bulgarian greeting (→ ChitChat), prose or an injected "answer CHITCHAT" instruction (→ Other), and an empty answer (→ Other)
- [x] 1.3 Add `Agent:IntentModel` and `Agent:IntentTimeoutSeconds` (default 5; 0 disables stage two) to `AgentOptions` and `appsettings`; cancel the call on timeout and on the request's token, map timeout/transport/provider failure to `Other` with a reason, and log at debug without question text; verify tests for a hanging model (times out → Other, turn still answers) and for `IntentTimeoutSeconds=0` (no model call)

## 2. Turn wiring and trace

- [x] 2.1 Await the classification in `ChatTurnRunner`, keep forcing to `Procedural`/`Mixed`, and emit the richer `intent` event (`stage`, `model`, `rawAnswer`, `durationMs`, title naming the stage); verify tests: rules-decided turn traces `stage: "rules"` with null duration, model-decided turn traces the stage, raw answer and duration, and the classifier's own call does not appear as a `model.request`
- [x] 2.2 Update `ApiFactory`'s scripted model so it answers classification requests (label for the question) and keeps answering turns as before; verify the existing agent/chat/trace tests pass and a Bulgarian question end-to-end through the API forces `search_documents`

## 3. CI stub

- [x] 3.1 Teach `compose/ollama-stub/server.py` to detect the classifier's system marker and reply with a single label (English plus the Bulgarian words used in the checks); verify `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e` passes and a Bulgarian question through the CI stack returns sources

## 4. Verification and docs

- [x] 4.1 In the running stack on :7171: ask the Bulgarian questions from the reported conversation and confirm the monitor shows `Intent Procedural (model, …)` with forced retrieval and sources, that an English question still shows `rules`, and that a greeting is not forced. Run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`
- [x] 4.2 Run `make eval-selection` and `make eval-injection` against the running stack (intent drives forced retrieval, so selection must be re-measured); record the numbers in DECISIONS.md and stop if either falls below its threshold
- [x] 4.3 Update `docs/trace-events.md` (intent payload), README (behind-the-scenes intent line) and DECISIONS.md (new section: two-stage classifier, why not model-only, timeout and fallback, configuration); verify the sections exist

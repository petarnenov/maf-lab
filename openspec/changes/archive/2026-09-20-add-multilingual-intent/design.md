# Design

## Context

`IntentClassifier.Classify` is a pure function over the question: four English regexes decide between `Procedural`,
`Mixed`, `Data`, `ChitChat` and `Other`. `ChatTurnRunner` calls it before the first model call and uses the result
for exactly two things — forcing `search_documents` for `Procedural`/`Mixed` (`ChatTurnRunner.cs:72`) and the
`NoToolOnHowWhy` production signal (`TurnSignals.cs:12`). See proposal.md for why `Other` wins in practice.

The model seam already exists: `IChatClientFactory.CreateChatClient(model)` builds a client for any model name, and
tests replace the whole factory with a scripted client. The turn's chat client is wrapped in `TracingChatClient`,
which records `model.request`/`model.response`; the classifier must *not* go through that wrapper, or its call would
appear in the monitor as one of the turn's own model calls.

## Goals / Non-Goals

**Goals:**
- A Bulgarian (or any other) procedural question forces retrieval exactly as its English twin does.
- Turns the rules already recognise stay free: no extra call, no added latency.
- The classifier can never do more than pick one of the five intents; anything else degrades to today's behaviour.
- The monitor shows which stage decided and what it cost.

**Non-Goals:**
- Translating questions, answering in a chosen language, or detecting the language as a separate feature.
- Replacing the rules with a model, or adding a fine-tuned/dedicated classification service.
- Changing what an intent *does* (forcing is still `Procedural`/`Mixed` only).
- Multilingual retrieval quality — the corpus is English; a Bulgarian question is embedded as-is, and that is
  measured, not fixed, here.

## Decisions

### Two stages, rules first — not a model for every turn
Rules cover the English questions the evals and CI use, at zero cost, and keep the deterministic path that
`make ci-e2e` depends on. The model stage runs only on `Other`, which today is ~every non-English turn and a
handful of unusual English ones. Alternatives: model-only (a model call on every "hi", and CI would need a stub
that classifies before it can answer) and rules-only in both languages (cheapest, but every new language is a code
change, and Bulgarian morphology in a regex is a poor substitute for understanding).

### The classifier is a separate, non-streaming chat call with its own client
`IntentClassifier` becomes an injectable service (`IIntentClassifier`) that keeps the static rules and adds the
model stage. It builds its client from `IChatClientFactory.CreateChatClient(options.IntentModel)`, bypassing
`TracingChatClient`, and calls `GetResponseAsync` (no streaming, no tools, `Temperature = 0`, `MaxOutputTokens`
small). The turn's own client and options are untouched, so the monitor's Model tab keeps showing only the turn's
real calls; the classification is visible in the `intent` event instead.

### Prompt shape: labels out, question in as data
The system message lists the five labels with a one-line meaning each and demands exactly one label as the whole
answer, in any language of input. The question goes in a user message wrapped in a delimiter (the same
`<user_question>` style the envelope already uses for tool data), with a standing instruction that its content is
data to classify and never an instruction. Output handling is the real defence: the answer is trimmed, upper-cased,
stripped of punctuation and matched against the five names; anything else — prose, a refusal, an injected command,
an empty string — becomes `Other`. This is also why no structured-output/JSON-schema mode is needed: a one-word
answer is easier to validate than to constrain, and it works on every provider.

### Failure is `Other`, and the turn never waits long
The call runs with a linked `CancellationToken` carrying `AgentOptions.IntentTimeout` (default 5 s; the turn's own
budget is minutes, so this is a small tail). Timeout, transport error, provider error and unusable text all return
`Other` with a reason, and are logged at debug without the question text (no message content in logs). The turn then
behaves exactly as it does today.

### Model choice is configuration, defaulting to the chat model
`Agent:IntentModel` (empty → `Models:ChatModel`) and `Agent:IntentTimeoutSeconds`. Default keeps the lab on one
model and one endpoint, so Ollama Cloud's key is the only secret; a smaller local model can be pointed at without a
code change when latency matters.

### The CI stub answers classification requests
The stub recognises the classifier's system message (a fixed marker string) and replies with one label derived from
the same kind of keyword check, extended with the Bulgarian words the tests use. This keeps `make ci-e2e` model-free
and deterministic while actually exercising the second stage, instead of leaving it untested outside unit tests.

### Trace and signals
The `intent` event gains `stage` (`rules` | `model`), `model`, `rawAnswer` and `durationMs` (null for the rules
stage), and its title reads e.g. `Intent Procedural (model, 180 ms) → forcing search_documents`. `TurnSignals` is
unchanged: a `Procedural` decided by the model counts as how/why like any other.

## Risks / Trade-offs

- **A wrong model classification forces retrieval on a greeting, or skips it on a procedural question** → forcing is
  the only effect and the tool result is just context; `selection` evals are re-run for the change, and the rules
  still short-circuit everything they already recognise.
- **Injection through the question aimed at the classifier** → the classifier has no tools, its output is validated
  against a closed set, and it never reaches the answer; worst case is a wrong label, which is the pre-change
  behaviour. Covered by a scenario and by re-running the `injection` suite.
- **Extra latency on non-English turns** → capped by `IntentTimeout`, on a small model, and only for turns that the
  rules do not recognise; a timeout degrades instead of failing.
- **Cost of one more model call per unrecognised turn** → accepted (the alternative is a call on every turn), and
  the trace makes it visible.
- **The stub's classifier diverges from a real model** → CI checks the wiring and the fallbacks, not the model's
  judgement; judgement is covered by the on-demand evals.

## Migration Plan

Stateless: no schema, no stored data, no API change. The classifier is constructed per API replica and both replicas
pick it up on the next `make up`. Rollback is `Agent:IntentTimeoutSeconds=0` (or reverting the commit), which
disables the model stage and restores the rules-only behaviour.

## Open Questions

- Whether a smaller/cheaper model than `gpt-oss:120b` is enough for the second stage — answerable from the
  `selection` eval numbers and the traced durations after the change is running, without touching the specs.

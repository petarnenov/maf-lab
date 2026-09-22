# Proposal

## Why

Every turn is classified before the first model call, and for a question the English rules do not
recognise that classification is a model call of its own. Measured over the 137 turns in
`TurnTraces`:

- 114 turns record which stage decided. **57 rules, 57 model** — an even split.
- By language: Bulgarian **37 of 37 reach the model**; English 20 of 77. The rules in
  `IntentClassifier.Classify` are English regexes, so every question in Cyrillic falls through.
- The model stage costs a median of **1053 ms** (p90 1435 ms, max 4003 ms, 66.8 s across 57 calls).
  The medians are the same in both languages — 1022 ms Bulgarian, 1066 ms English. The cost is not
  the question. It is the model.

`ModelIntentClassifier` says so itself, in a comment above `MaxOutputTokens = 512`:

> One word is the whole answer, but a reasoning model spends its budget thinking first
> (gpt-oss:120b needs ~60 tokens for this prompt and ignores think=false)

A second is spent reasoning toward a single word. And most of those words change nothing. The model
stage decided `Other` 33 times, `Procedural` 14, `ChitChat` 6, `Data` 3, `Mixed` 1. The only
consumers of `Intent` are `ForcesRetrieval` and `IsHowWhy`, both of which read only `Procedural` and
`Mixed`. So **15 of 57 calls changed the turn**; the other 42 arrived at a label behaviourally
identical to `Other` — which is also what the system returns when the stage is switched off
entirely. Three of those 42 did not even produce a known intent.

The mechanism to fix this already exists and has never been configured. `AgentOptions.IntentModel`
is `""`, and its comment says "empty uses the chat model". The classifier runs on `gpt-oss:120b`
because nobody chose otherwise, not because anybody decided it should.

## What Changes

- The classification model becomes an explicit choice, stated in configuration, rather than
  whatever the answering model happens to be. Today, changing `CHAT_MODEL` silently changes how
  questions are classified; after this, it does not.
- The choice is made by measurement across the models the chat endpoint serves, against
  `make eval SUITE=selection` for quality and the trace's own `durationMs` for cost. A candidate is
  accepted only if it holds recall, precision, exactMatch and negative accuracy at their current
  values — all four are 1.0 today.
- If the chosen model does not reason before answering, the 512-token output budget and the system
  prompt are revisited — but only where the eval supports it, not on the assumption that a smaller
  model needs a different prompt.
- If no candidate holds the quality, that is the result. The sweep is recorded and the classifier
  keeps the model it has. A model chosen for its latency while quietly misclassifying questions
  would trade a second of visible delay for silently unforced retrieval, which is worse.

Nothing about *what* the classifier does changes: the two stages, the injection-resistant framing,
the timeout fallback and the "unrecognised answer forces nothing" rule all stay exactly as they are.
Holding them is the acceptance criterion.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. This change declares `skip_specs: true`.

The `chat-agent` requirement "Conditional forced retrieval" already governs everything here: it
requires two stages, a model "asked for one of the known intents", classification that does not
depend on the question's language, and defined behaviour when the model fails, times out or answers
something unrecognised. It deliberately says nothing about *which* model, and nothing about cost —
and it should not, because the answer is empirical and will change as models do. Choosing the model
and writing it into configuration changes no behaviour that specification describes; every scenario
under it must pass unchanged afterwards, which is what makes this a configuration change rather
than a spec change.

## Impact

- `src/Maf.Lab.Api/Agent/AgentOptions.cs` — `IntentModel` gains a chosen default.
- `compose/docker-compose.yml` — the value surfaced alongside `Models__ChatModel`, so a deployment
  can override it the way it overrides the chat model.
- `src/Maf.Lab.Api/Agent/ModelIntentClassifier.cs` — only if the sweep shows the output budget or
  prompt should differ for a non-reasoning model.
- `DECISIONS.md` — the chosen model and the numbers behind it.
- `evals/baseline.json` — only if selection metrics move at all; the intent is that they do not.
- No new dependency, no package version moves, no API or schema change.

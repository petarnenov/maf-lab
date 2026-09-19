# Proposal

## Why

Intent classification decides whether a turn is forced to call `search_documents`, and it is a set of English
regexes over the raw question (`IntentClassifier`). A Bulgarian question matches nothing and falls through to
`Other`, so no retrieval is forced: in a real conversation on the running stack, "Каква е процедурата за билинг
фее" was classified `Other` while its English twin is `Procedural`. The assistant then only answers from documents
when the model happens to choose the tool by itself — in that same conversation it did so twice out of six turns.
The lab is meant to exercise retrieval-as-a-tool, and half the questions silently skip it.

## What Changes

- Keep the existing rules as the first stage: when they classify a question (`Procedural`, `Mixed`, `Data`,
  `ChitChat`), nothing else runs — no extra latency, no tokens.
- When the rules fall through to `Other`, a **second stage asks a small chat model** to classify the question into
  the same five intents, in whatever language it is written. Its answer is accepted only when it is one of the
  known labels.
- A failure, a timeout or an unrecognised label leaves the intent `Other`, exactly as today: nothing is forced and
  the model is still free to call the tool itself.
- The trace records which stage decided (`rules` or `model`), how long the model stage took, and the raw label it
  returned, so the monitor shows why a turn was or was not forced.
- The classifier model and its timeout are configuration, defaulting to the chat model; the CI stub learns to
  answer a classification request so the path is covered without a real model.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `chat-agent`: "Conditional forced retrieval" gains the second classification stage and states that intent
  detection is language-independent, that an unusable classification is treated as no intent, and that the
  classifier's output can never be anything but one of the known intents.
- `turn-tracing`: the intent event must also carry the stage that decided, the model stage's latency, and the raw
  label when a model was asked.

## Impact

- `src/Maf.Lab.Api/Agent/IntentClassifier.cs` — rules stay; a new model-backed stage is added behind them.
- `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs` — awaits the classification and traces the richer intent event.
- `src/Maf.Lab.Api/Agent/AgentOptions.cs` (+ `appsettings`) — model name and timeout for the second stage.
- `compose/ollama-stub/server.py` — recognises a classification request and answers with a label, so CI exercises
  the model stage without a model.
- `docs/trace-events.md`, `README.md`, `DECISIONS.md` — the two-stage classifier and why it is not model-only.
- Tests: `tests/Maf.Lab.Tests/AgentUnitTests.cs` (Bulgarian cases through a scripted classifier), `ApiFactory`
  (the scripted model must answer classification requests). Evals: `selection` must be re-run, since forced
  retrieval changes what the model is asked to do.
- No change to tenancy, the query path, tool schemas or the HTTP contract.

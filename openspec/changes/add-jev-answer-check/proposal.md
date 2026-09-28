# Proposal

## Why

Jev reads everything on the way in: the question (intent, domains, routing, the prompt screening), every tool result
(the content guard) and every search (the relevance gate). It never reads what comes out. Nothing in a turn says
whether the model's final answer addressed the question, or whether what it claimed was in the data it was given. A
hallucinated fee figure or an answer about something else reaches the review queue today only if the user complains or
rephrases. The generation eval's LLM rubric measures faithfulness and relevance, but only on demand and only on its own
dataset, never on production turns.

## What Changes

- **One Jev request after the answer.** When a turn that reached the model ends with a non-empty answer, the api sends
  one request (the shared `JevClient`: same endpoint, pinned model, credential and named client) with two Nouls about
  named state fields `user_question`, `answer` and `sources`:
  - `answer_relevant`: does `answer` address `user_question`?
  - `answer_grounded`: is every factual claim in `answer` supported by `sources`?
- **`sources` is what the model read.** Every data envelope the model was handed this turn, after the content guard: a
  search's excerpts one by one, any other tool's result whole, a withheld item never. A turn with no tool result is
  checked against an empty `sources`; an answer that makes no factual claim, or says it does not know, is grounded.
- **Not for every turn.** A refused prompt, a turn that ends waiting for a person's confirmation, a failed turn and an
  empty answer run no check.
- **It cannot block.** The answer has already streamed. The outcome is a trace event and, below a floor, a review
  signal. The check runs before the trace is stored and before the terminal AG-UI event, so the stored trace has it;
  its latency adds to the turn's.
- **Configurable, fail-open.** `Jev:AnswerCheck` — `Enabled` (true), `TimeoutSeconds` (3), `MinRelevant` (0.5),
  `MinGrounded` (0.5), `MaxSourceChars` (12000). Disabled, no key, a timeout, an error or an incomplete answer records
  `unchecked` with the reason and never fails the turn. The floors are provisional until measured.
- **Trace:** a new `answer.check` event with both probabilities, both floors, the verdict (`pass`, `not_relevant`,
  `not_grounded`, `unchecked`), the model, the reason, how many sources and characters were judged and the request
  count; its duration is the request's latency. Never the answer text or the sources' text.
- **Review signals:** `answer_not_relevant` and `answer_not_grounded`, one per floor missed, so the turn enters the
  existing review queue.
- **Jev statistics:** `answer` becomes a request-bearing site in the overview (requests, unavailable, latency), and a new
  Answer check section counts checked turns, the share not relevant and not grounded, unchecked turns and the latency.
- **Monitor:** the event draws its own timeline bar (its duration is the Jev request's latency) in a kind colour of its
  own, titled with Jev, both probabilities against their floors and the verdict; a failed verdict shows as a header
  chip ("answer: not grounded").
- **Eval:** the `generation` suite also reads each case's Jev verdict from the turn and reports how often Jev and the
  rubric agree, as extra metrics.

## Capabilities

### New Capabilities

- `answer-check`: Jev's post-answer relevance and grounding check — when it runs, what it reads, what it records, how
  it fails.

### Modified Capabilities

- `turn-tracing`: a new requirement for the `answer.check` event.
- `jev-statistics`: the answer check as a request-bearing site and its own section.
- `eval-harness`: the generation suite reports Jev's verdict beside the rubric.
- `web-ui`: the monitor shows the check (timeline row with its own colour and latency bar, a header chip when flagged)
  and `/admin/jev` shows the `answer` site and an Answer check section.

## Impact

- `src/Maf.Lab.Api/Agent/Jev/JevAnswerCheck.cs` (new: options, questions, the check), `ChatTurnRunner.cs` (collect
  what the model read, run the check, trace, signals), `JevIntentClassifier.cs` (registration), `TurnSignals` constants
  in `Maf.Lab.Domain/Feedback/FeedbackContracts.cs`, `TraceKinds.AnswerCheck`.
- `JevStatistics.cs`, `Maf.Lab.Domain/Jev/JevStatsContracts.cs` (an optional `AnswerCheck` section).
- `src/Maf.Lab.Eval/Suites/GenerationSuite.cs`.
- `web/src/monitor/*` (kind colour, header chip), `web/src/jev/JevPage.tsx`, `web/src/api/types.ts`, the review
  queue's signal labels.
- `tests/Maf.Lab.Tests/FakeJev.cs` and `compose/ollama-stub/server.py` answer the two Nouls; tests that count Jev
  requests per turn account for the extra one.
- `docs/trace-events.md`, `DECISIONS.md`. No package added or moved; no model setting changed.
- Cost: one more Jev request per answered turn (~2–4k input tokens with sources), up to the 3 s budget added to the
  turn's duration.

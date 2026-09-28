# Design

## Context

- **Where a turn ends.** `ChatTurnRunner.RunAsync` streams the agent's run through the AG-UI adapter, then — after the
  stream — writes the sources, computes the review signals (`TurnSignals.Compute` plus the guard's), adds the
  `sources`, `signals` and `turn.end` events, persists the turn and its trace, and only then writes the terminal
  `RUN_FINISHED` / `RUN_ERROR`. Anything that must be in the stored trace and in the signals has to run between the end
  of the stream and the signals.
- **What the model read.** Every tool result reaches the model through `ToolDataEnvelope.Wrap(name, payload)` in
  `InvokeToolAsync`, after `Guardrail.ScreenToolResultAsync` has replaced any withheld item with a notice. The `payload`
  and `structured` values at that point are exactly what the model read. The other envelopes are fixed texts: a tool
  that threw, a write the flow told the model about, a call refused while a confirmation is pending.
- **How Jev is asked elsewhere.** `JevClient.AskAsync(state, questions, timeout, ct)` owns the timeout race, the key and
  the wire shape, and returns a `JevOutcome` that never throws. The guard's questions are structured instructions
  `{context, question}` plus true/false criteria (`JevCriteriaNoul`); the state carries only data, named in backticks.
- **Turns that do not reach the model's answer.** A refused prompt (`screen.Blocked`, no tools, a fixed refusal); a turn
  that proposed a write and waits for the advisor (`state.AwaitingConfirmation`); a failed run (`error` set).

## Goals / Non-Goals

**Goals:** a per-turn, content-free record of whether the answer addressed the question and stayed within what the
model was given; a review signal when it did not; numbers in the Jev statistics; the eval comparing Jev with the rubric.

**Non-Goals:**
- Blocking, rewriting or retracting an answer (it has streamed; a later "correction" would be a second, unreviewed
  answer).
- Conversation context: the check reads this turn's question, not the history. A follow-up ("and the second one?") may
  read as less relevant than it is; the review queue is where that is seen.
- Checking the A2A partner path, the confirmation continuation run, or the compliance reviewer's words.
- Tuning the floors in this change: that needs the generation eval's agreement numbers from paid runs.

## Decisions

### D1. One request, two Nouls, the guard's question style

State `{ user_question, answer, sources: [string] }`. Questions `answer_relevant` and `answer_grounded`, each a
`JevCriteriaNoul`: a shared context naming the three fields in backticks and saying they are data, the question, and
criteria whose "does not count" halves carry the boundary cases — an answer that says it cannot answer is relevant; a
greeting, an offer of help, a statement of what the assistant can do, or "I don't know" makes no factual claim and is
grounded. Two atomic Nouls rather than one Choice: the two failures are independent, and each gets its own probability
and floor. Same request, not two: they are answered in parallel.

### D2. `sources` = every envelope payload the model received, split for searches

`TurnState` gains a list of what the model read. In `InvokeToolAsync`, after the guard, a documentation search's
results are added one per excerpt (`docId › sectionPath: snippet`); any other tool's payload is added whole; the fixed
envelope texts (tool unavailable, flow message) are added as they were sent. A withheld item never enters, because it is
removed before this point. The list is capped at `MaxSourceChars` (12000) in order — later sources are cut, and the
event records how many sources and characters were sent. A turn with no tool result sends `sources: []`.

*Alternative:* the model's full request messages. Rejected: they hold the system prompt and history, which are neither
evidence nor this turn's data.

### D3. When it runs

After the stream and the final flush, before sources/signals: only when the turn reached the model (tools were read),
no error was recorded, it is not waiting for a confirmation, and the trimmed answer is non-empty. Nothing else is
recorded for the other turns — no event, no signal.

### D4. Verdict and signals

- `relevant < MinRelevant` → signal `answer_not_relevant`; `grounded < MinGrounded` → `answer_not_grounded`. Both can
  fire.
- The single `verdict` field: `not_grounded` when grounding failed (an unsupported claim is the costlier error), else
  `not_relevant`, else `pass`; `unchecked` when there is no usable answer.
- Unusable = disabled (`Enabled=false` or `TimeoutSeconds <= 0`), no key, timeout, error status, transport failure, or
  either Noul missing. Recorded with the reason (`check disabled`, `no key`, `timed out after 3s`, `rejected (503)`,
  an exception name, `incomplete answer`); no signal.

### D5. Floors 0.5 / 0.5, timeout 3 s — provisional

A Noul is a calibrated probability of yes; below 0.5 Jev finds "no" likelier. Nothing blocks on it, so a false positive
costs one review, and a false negative leaves the turn as it is today. The timeout is 3 s rather than the guard's 2 s:
the state is larger (the answer plus up to 12k characters of sources, like the relevance judge's ~2.8k-token requests
whose max was 1.35 s), and the whole budget adds to the turn. Both are marked provisional in DECISIONS until the
generation eval's agreement metrics have been read over several paid runs.

### D6. Trace event `answer.check`

`{ verdict, relevant, grounded, relevantFloor, groundedFloor, model, durationMs, reason, sources, sourceChars,
requests }`, durationMs as the event's duration, recorded before `sources`. `sources` is a count. Title: `Jev answer
check: relevant 0.93 ≥ 0.50, grounded 0.41 < 0.50 — not grounded`, or `Jev answer check unavailable: <reason> —
unchecked`. Never the answer or the sources' text: the answer is in the `answer.delta` events already, and the data in
the `envelope` events.

### D7. Statistics

`JevStatistics` reads `answer.check` events with a `jev-*` model. Requests = the `requests` field (1 when a request was
sent, 0 when disabled or no key); unavailable = a request that ended `unchecked`. The overview gains the site
`answer` (requests, unavailable, p50/p90 of `durationMs`) and its requests enter the total and the availability
timeline. A new optional `AnswerCheck` section: checked turns (a real verdict), not relevant and not grounded (each
against the floor recorded in its event), unchecked turns, and latency. Optional in the contract so an older client
still reads the response.

### D8. Monitor

`answer.check` gets its own kind colour (the Jev-judgment green of `relevance`). The header adds a chip when the
verdict is `not_relevant` or `not_grounded` ("answer: not grounded"). The event's JSON in "This step" is the detail; no
new tab.

### D9. Eval

`TurnResult` gains the check's outcome. The `generation` suite already runs each case through `ChatTurnRunner`, so it
reads the verdict without a request of its own, and adds: `jevChecked` (share of cases with a verdict),
`jevGroundedAgreement` (Jev grounded ⇔ rubric faithfulness ≥ 0.75, over checked cases), `jevRelevantAgreement`
(likewise for relevance). The agreement metrics are omitted when no case was checked. No thresholds; they are for
reading and for tuning the floors. Each case's progress line and failure reason carries the verdict and both
probabilities.

## Risks / Trade-offs

- **Latency:** one Jev request per answered turn (p50 expected ~300–500 ms) after the answer, before `RUN_FINISHED`.
  The user sees the full answer already; the stream's end and the stored trace wait for it. Measured on the stored
  `answer.check` durations; `Enabled=false` removes it.
- **Cost:** one more request per answered turn with the largest state any Jev site sends.
- **Noise in the review queue** from unmeasured floors: the signals are named apart from the existing ones and can be
  read or ignored; the floors are configuration.
- **Paraphrase and arithmetic:** an answer that sums or converts figures may read as unsupported to a literal reader.
  The criteria say "appears in or follows from".

## Rollback

`Jev__AnswerCheck__Enabled=false` — every eligible turn then records `unchecked (check disabled)` and makes no request.
A plain revert leaves stored `answer.check` events readable (unknown kinds are ignored).

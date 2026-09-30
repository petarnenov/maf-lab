## MODIFIED Requirements

### Requirement: Guard decisions are traced
Every screening a turn performs SHALL be recorded in its trace as a `guardrail` event: which check it was (the prompt, a
tool result, a reviewer's words), the tool and call it concerned when there was one, the probability of every question
asked for every item assessed, the decision (pass, blocked, withheld or unscreened), the threshold it was taken
against, how many items were withheld, the versioned model that answered, how long it took and — when the text was
unscreened — why. For a codebase search it SHALL also record which context the items were screened in and which
questions were record-only, so a record-only score at or above the threshold is visible without having withheld
anything. The event MUST NOT contain the assessed text a second time, nor any credential.

When the guard blocks or withholds, the turn SHALL NOT perform or record the downstream step for the blocked or
withheld item: the trace holds only the guard's decision and, where the model needs one, a neutral notice — never the
blocked prompt, the withheld content, or the work that would have followed it. Concretely:
- A turn whose prompt is blocked calls no model and runs no tool. Its trace SHALL hold turn start, intent, the
  `guardrail` event, the refusal as `answer.delta`, sources, signals and turn end, and it SHALL NOT hold the `prompt`
  event (the system prompt text and the tool schemas), nor any history, tool-call or data-envelope event — neither in
  the live stream nor in the stored trace.
- A withheld tool-result item SHALL be absent from the `tool.result` event as well as from the model's data envelope
  and the turn's sources: the recorded result SHALL be the redacted one the model may read — for a search, the stub
  that keeps only the item's non-text identifiers (a path and line range, or a document id) and the neutral notice
  with a count — never the withheld excerpt, snippet, symbol, section heading or record.
- A flagged reviewer's or other agent's words SHALL NOT be recorded in the trace, and the trace SHALL show only the
  neutral failed-review outcome.
- A blocked A2A partner prompt SHALL be refused with no tool fetch, no prompt build and no model call, and SHALL leave
  no trace of the blocked question.

#### Scenario: A refused prompt
- **WHEN** a prompt is refused
- **THEN** the trace holds a `guardrail` event for the prompt with the scores, the decision `blocked` and the threshold, and no model call

#### Scenario: A blocked prompt omits the system prompt and tools
- **WHEN** a prompt is blocked
- **THEN** neither the streamed trace nor the stored trace holds a `prompt` event, so it carries no system prompt text and no tool schema, and it holds no history or envelope event either

#### Scenario: A withheld excerpt
- **WHEN** one excerpt of a search result is withheld
- **THEN** the trace holds a `guardrail` event for that tool call with each excerpt's scores, the decision `withheld` and one withheld item, and the envelope event shows what the model received instead

#### Scenario: A withheld excerpt's content stays out of the trace
- **WHEN** one excerpt of a search result is withheld
- **THEN** the withheld excerpt's text appears in neither the `tool.result` event nor the envelope event, which carry the stub and the neutral notice instead

#### Scenario: A record-only score on a codebase snippet
- **WHEN** a codebase snippet scores 0.97 on the record-only question and passes
- **THEN** the `guardrail` event records the codebase context, the record-only question, the 0.97 and the decision `pass`

#### Scenario: A flagged reviewer's words stay out of the trace
- **WHEN** a compliance reviewer's reason or question is flagged by the guard
- **THEN** the trace records the review as failed with a neutral outcome and holds none of the reviewer's flagged words

#### Scenario: A blocked partner leaves no trace of its question
- **WHEN** an A2A partner's question is blocked by the guard
- **THEN** the partner receives the fixed refusal with no model call, and no trace records the blocked question, the system prompt or the tool schemas

#### Scenario: Screening unavailable
- **WHEN** a screening request times out
- **THEN** the `guardrail` event records the decision `unscreened` and the reason

### Requirement: The answer check is traced
A turn whose answer was checked by Jev (as specified by `answer-check`) SHALL record one `answer.check` event, before
the turn's sources, signals and end. It SHALL carry the relevance and grounding probabilities, both signal floors
(`relevantFloor`, `groundedFloor`: below them a review signal is raised), both pass thresholds, the verdict (`pass`,
`uncertain`, `not_relevant`, `not_grounded`, `unchecked`), the context it was asked in (billing or codebase), the
versioned model, the reason when unchecked, how many sources and characters were judged, how many previous sources,
how many duplicates were dropped, and how many Jev requests the check made. The event's duration SHALL be the check's
latency, so the timeline draws it as a bar of its own. Its title SHALL name Jev, both probabilities against their
band and the verdict, or, when unchecked, the reason. The event SHALL NOT contain the answer's text or the sources'
text. A turn that ran no check SHALL record no `answer.check` event.

#### Scenario: A checked answer in the trace
- **WHEN** a procedural turn is answered and Jev checks the answer
- **THEN** its trace holds an `answer.check` event after the model's response and before `sources`, `signals` and `turn.end`, with both probabilities, both floors, both pass thresholds, the verdict and a duration
- **AND** its title reads like "Jev answer check: relevant 0.95 ≥ 0.80, grounded 0.95 ≥ 0.80 — pass"

#### Scenario: An uncertain answer in the trace
- **WHEN** Jev's grounding probability is 0.35 inside the review band
- **THEN** the `answer.check` event's verdict is `uncertain`, its title reads like "… grounded 0.35 in 0.20–0.80 — uncertain", and the `signals` event carries no answer signal

#### Scenario: The event holds no content
- **WHEN** a turn's answer is checked
- **THEN** the `answer.check` event contains neither the answer's text nor any excerpt's text

#### Scenario: Unchecked is recorded with its reason
- **WHEN** the answer check times out
- **THEN** the `answer.check` event's verdict is `unchecked` and its reason says it timed out

#### Scenario: Over the cap is recorded
- **WHEN** a turn's deduplicated sources do not fit under the cap
- **THEN** the `answer.check` event's verdict is `unchecked`, its reason is `sources over cap`, and its `requests` is 0

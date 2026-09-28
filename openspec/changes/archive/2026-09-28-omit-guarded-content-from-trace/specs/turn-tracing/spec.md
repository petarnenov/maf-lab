# Spec Delta

## MODIFIED Requirements

### Requirement: Guard decisions are traced
Every screening a turn performs SHALL be recorded in its trace as a `guardrail` event: which check it was (the prompt, a
tool result, a reviewer's words), the tool and call it concerned when there was one, the probability of every question
asked for every item assessed, the decision (pass, blocked, withheld or unscreened), the threshold it was taken
against, how many items were withheld, the versioned model that answered, how long it took and — when the text was
unscreened — why. The event MUST NOT contain the assessed text a second time, nor any credential.

When the guard blocks or withholds, the turn SHALL NOT perform or record the downstream step for the blocked or
withheld item: the trace holds only the guard's decision and, where the model needs one, a neutral notice — never the
blocked prompt, the withheld content, or the work that would have followed it. Concretely:
- A turn whose prompt is blocked calls no model and runs no tool. Its trace SHALL hold turn start, intent, the
  `guardrail` event, the refusal as `answer.delta`, sources, signals and turn end, and it SHALL NOT hold the `prompt`
  event (the system prompt text and the tool schemas), nor any history, tool-call or data-envelope event — neither in
  the live stream nor in the stored trace.
- A withheld tool-result item SHALL be absent from the `tool.result` event as well as from the model's data envelope
  and the turn's sources: the recorded result SHALL be the redacted one the model may read (the neutral notice and a
  count), never the withheld excerpt or record.
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
- **THEN** the withheld excerpt's text appears in neither the `tool.result` event nor the envelope event, which carry the redacted result and the neutral notice instead

#### Scenario: A flagged reviewer's words stay out of the trace
- **WHEN** a compliance reviewer's reason or question is flagged by the guard
- **THEN** the trace records the review as failed with a neutral outcome and holds none of the reviewer's flagged words

#### Scenario: A blocked partner leaves no trace of its question
- **WHEN** an A2A partner's question is blocked by the guard
- **THEN** the partner receives the fixed refusal with no model call, and no trace records the blocked question, the system prompt or the tool schemas

#### Scenario: Screening unavailable
- **WHEN** a screening request times out
- **THEN** the `guardrail` event records the decision `unscreened` and the reason

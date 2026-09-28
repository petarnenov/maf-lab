# Spec Delta

## ADDED Requirements

### Requirement: Guard decisions are traced
Every screening a turn performs SHALL be recorded in its trace as a `guardrail` event: which check it was (the prompt, a
tool result, a reviewer's words), the tool and call it concerned when there was one, the probability of every question
asked for every item assessed, the decision (pass, blocked, withheld or unscreened), the threshold it was taken
against, how many items were withheld, the versioned model that answered, how long it took and — when the text was
unscreened — why. The event MUST NOT contain the assessed text a second time, nor any credential. A refused turn's
trace SHALL still hold turn start, intent, the `guardrail` event, the refusal as `answer.delta`, signals and turn end.

#### Scenario: A refused prompt
- **WHEN** a prompt is refused
- **THEN** the trace holds a `guardrail` event for the prompt with the scores, the decision `blocked` and the threshold, and no model call

#### Scenario: A withheld excerpt
- **WHEN** one excerpt of a search result is withheld
- **THEN** the trace holds a `guardrail` event for that tool call with each excerpt's scores, the decision `withheld` and one withheld item, and the envelope event shows what the model received instead

#### Scenario: Screening unavailable
- **WHEN** a screening request times out
- **THEN** the `guardrail` event records the decision `unscreened` and the reason

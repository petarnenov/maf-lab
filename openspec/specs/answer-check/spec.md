# answer-check Specification

## Purpose
Jev's check of the model's final answer: after a turn has answered, one Jev request asks whether the answer addressed
the question and whether every factual claim in it is supported by the data the model read that turn. The outcome is
recorded and can flag the turn for review; it never changes or blocks the answer.

## Requirements

### Requirement: The final answer is checked by Jev
When a chat turn reached the model and ended with a non-empty answer, the api SHALL send one Jev request, through the
same client, endpoint, pinned model and credential as every other Jev call, carrying two yes/no questions about named
fields of the request's state:
- `answer_relevant`: whether `answer` addresses `user_question`, read together with `previous_question` (the
  conversation's previous question, empty on its first turn) when it follows up on it;
- `answer_grounded` (against `sources` and `previous_sources`, what the model read for the previous question, taken
  from that turn's data envelopes): whether every factual claim in `answer` is supported by `sources`.

The state SHALL hold the user's question, the answer and the sources as data. The question, the answer and the sources
SHALL NOT be placed in any question's instructions or criteria, and the instructions SHALL name the fields they judge.
An answer that makes no factual claim, or says it does not know, SHALL count as grounded.

The check SHALL NOT run for a refused prompt, a turn that ends waiting for a person's confirmation, a failed turn, or a
turn whose answer is empty.

#### Scenario: An answered turn is checked
- **WHEN** a procedural question is answered from a search's excerpts
- **THEN** exactly one Jev request after the answer carries `answer_relevant` and `answer_grounded`, with the question, the answer and the excerpts in its state

#### Scenario: A refused prompt is not checked
- **WHEN** the content guard refuses the prompt
- **THEN** no answer check is requested or recorded

#### Scenario: A turn waiting for a person is not checked
- **WHEN** a turn proposes a fee adjustment and ends waiting for the advisor's confirmation
- **THEN** no answer check is requested or recorded

#### Scenario: Nothing to cite
- **WHEN** a turn called no tool
- **THEN** the check is still asked, with an empty `sources`

### Requirement: Sources are what the model read
`sources` SHALL be the data the model received this turn, after the content guard: each excerpt a documentation
search returned, and the whole result of any other tool, as handed to the model. An item the content guard withheld
SHALL NOT be sent. The sources SHALL be capped at a configured number of characters, and the check SHALL record how
many sources and characters it sent.

#### Scenario: A withheld excerpt is not a source
- **WHEN** the content guard withheld one excerpt of a search and the model answered from the rest
- **THEN** the withheld excerpt's text is not in the answer check's request

### Requirement: The answer check flags, never blocks
The check SHALL run after the answer has been sent and SHALL NOT change, delay the content of, or retract it. It SHALL
finish before the turn's trace is stored and before the run's terminal event. Its outcome SHALL be:
- a verdict: `pass`; `not_grounded` when the grounding probability is below its floor; otherwise `not_relevant` when
  the relevance probability is below its floor; `unchecked` when there is no usable answer;
- the review signal `answer_not_grounded` when grounding is below its floor, and `answer_not_relevant` when relevance
  is below its floor, so the turn enters the review queue.

The floors, the timeout and whether the check runs SHALL be configuration.

#### Scenario: A grounded, relevant answer
- **WHEN** Jev finds the answer relevant and grounded above both floors
- **THEN** the verdict is `pass` and no answer signal is added

#### Scenario: An unsupported claim
- **WHEN** Jev's grounding probability is below the floor
- **THEN** the verdict is `not_grounded`, the turn carries `answer_not_grounded`, and it appears in the review queue

### Requirement: The answer check fails open
When the check is disabled, there is no Jev key, the request times out, is rejected or fails, or Jev leaves either
question unanswered, the turn SHALL complete exactly as it would without the check, and the outcome SHALL be recorded as
`unchecked` with the reason. No review signal SHALL be added for an unchecked answer.

#### Scenario: Jev is down
- **WHEN** the answer check's request is rejected
- **THEN** the user receives the full answer, the run finishes successfully, and the check is recorded as `unchecked` with the reason

#### Scenario: Switched off
- **WHEN** the answer check is disabled
- **THEN** no request is sent and the check is recorded as `unchecked` because it is disabled

### Requirement: No message content from the answer check
Logs SHALL NOT carry the question, the answer or the sources' text. The Jev key SHALL travel only as the request's
bearer header.

#### Scenario: Logs carry structure only
- **WHEN** a turn's answer is checked
- **THEN** no log line contains the answer's text

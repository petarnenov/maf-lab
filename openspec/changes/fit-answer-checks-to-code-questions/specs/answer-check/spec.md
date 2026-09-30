## MODIFIED Requirements

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

The answer placed in the state SHALL be a normalised copy: the hyphen characters U+2010, U+2011 and U+2012 replaced by
`-`, U+2013 between two digits replaced by `-`, and the no-break spaces U+00A0 and U+202F replaced by a space. The
answer the user received and the stored answer SHALL NOT change.

The check SHALL NOT run for a refused prompt, a turn that ends waiting for a person's confirmation, a failed turn, or a
turn whose answer is empty.

#### Scenario: An answered turn is checked
- **WHEN** a procedural question is answered from a search's excerpts
- **THEN** exactly one Jev request after the answer carries `answer_relevant` and `answer_grounded`, with the question, the answer and the excerpts in its state

#### Scenario: Non-breaking hyphens in a cited place
- **WHEN** the answer cites `src/Maf.Lab.Api/Agent/Guardrail.cs:153‑195` written with U+2011
- **THEN** the answer in the Jev state reads `src/Maf.Lab.Api/Agent/Guardrail.cs:153-195`, and the answer shown and stored keeps the character the model wrote

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
search returned, each snippet a codebase search returned (as its place — path, line range and symbol — followed by
its text), and the whole result of any other tool, as handed to the model. The text of an item the content guard
withheld SHALL NOT be sent; its stub MAY be.

The sources SHALL be deduplicated before they are sent: search items with the same place — path and line range for
code, document id and section for documentation — are sent once, and any other source, and each previous envelope, is
sent once per identical text. They SHALL be ordered: first this turn's sources the answer cites — for a codebase
snippet, the normalised answer contains its path or its file name — then the rest of this turn's, then
`previous_sources`, the cited ones first. Each source SHALL be sent whole or not at all.

The sources SHALL be capped at a configured number of characters. When one of this turn's sources, or a previous
source the answer cites, does not fit under the cap, the check SHALL send no request and SHALL record `unchecked` with
the reason `sources over cap`. Previous sources that are not cited MAY be left out to fit. The check SHALL record how
many sources and characters it sent, how many previous sources, and how many duplicates it dropped.

#### Scenario: A withheld excerpt is not a source
- **WHEN** the content guard withheld one excerpt of a search and the model answered from the rest
- **THEN** the withheld excerpt's text is not in the answer check's request

#### Scenario: The same snippet returned three times
- **WHEN** a turn's three codebase searches each return `src/Maf.Lab.Api/Agent/Jev/JevAnswerCheck.cs:229-252` among their snippets
- **THEN** that snippet is in `sources` once, and the check records two duplicates dropped

#### Scenario: A cited late result is kept
- **WHEN** a turn's first searches return redundant snippets that together fill most of the cap, a later search returns the snippet the answer cites by its file name, and all of this turn's deduplicated sources fit
- **THEN** the cited snippet is the first entry of `sources`, and no source is cut

#### Scenario: More than the cap
- **WHEN** this turn's deduplicated sources are longer than the cap
- **THEN** no Jev request is sent, the verdict is `unchecked` with the reason `sources over cap`, and no answer signal is added

#### Scenario: Previous sources fill what is left
- **WHEN** a follow-up turn's sources and the previous turn's cited envelope fit under the cap but an uncited previous envelope does not
- **THEN** the check runs with the cited previous envelope and records how many previous sources it sent

### Requirement: The answer check flags, never blocks
The check SHALL run after the answer has been sent and SHALL NOT change, delay the content of, or retract it. It SHALL
finish before the turn's trace is stored and before the run's terminal event. Each of the two probabilities SHALL be
read against a review band of two configured thresholds, a signal floor below which the answer fails and a pass
threshold at or above which it passes. Its outcome SHALL be:
- a verdict: `not_grounded` when the grounding probability is below its signal floor; otherwise `not_relevant` when the
  relevance probability is below its signal floor; otherwise `pass` when both probabilities are at or above their pass
  thresholds; otherwise `uncertain`; and `unchecked` when there is no usable answer;
- the review signal `answer_not_grounded` when grounding is below its signal floor, and `answer_not_relevant` when
  relevance is below its signal floor, so the turn enters the review queue. An `uncertain` verdict SHALL add no review
  signal.

The thresholds, the timeout and whether the check runs SHALL be configuration. The thresholds SHALL be the values
measured on the labelled answer set (eval-harness) and recorded in the decision log; until then they SHALL default
to a signal floor of 0.2 and a pass threshold of 0.8.

#### Scenario: A grounded, relevant answer
- **WHEN** Jev answers 0.9 for relevance and 0.85 for grounding, with pass thresholds of 0.8
- **THEN** the verdict is `pass` and no answer signal is added

#### Scenario: An uncertain answer (medium confidence)
- **WHEN** Jev's grounding probability is 0.35, between a signal floor of 0.2 and a pass threshold of 0.8, and relevance is 0.9
- **THEN** the verdict is `uncertain`, the trace records it, and the turn carries no answer signal

#### Scenario: An unsupported claim
- **WHEN** Jev's grounding probability is 0.15, below a signal floor of 0.2
- **THEN** the verdict is `not_grounded`, the turn carries `answer_not_grounded`, and it appears in the review queue

#### Scenario: Both below their floors
- **WHEN** grounding is 0.1 and relevance is 0.1
- **THEN** the verdict is `not_grounded` and the turn carries both `answer_not_grounded` and `answer_not_relevant`

## ADDED Requirements

### Requirement: A code answer is judged as code
When this turn's sources or the previous turn's sources include a codebase search's snippets, the check SHALL ask its
two questions in a codebase context and SHALL otherwise use the billing and portfolio context unchanged. The codebase
context SHALL say that the user asked about the lab's own repository, that each codebase source reads as its place —
path, line range and symbol — followed by the code, and that other sources may be document excerpts and records. The
grounding criteria SHALL count as supported a path, file name, line range, symbol, identifier or quoted code that a
source carries. They SHALL count an explanation as supported when a source's code or text shows what it states. They
SHALL judge meaning, not wording, so an answer in Bulgarian or Latin-script Bulgarian about English code and comments
counts as supported when the code supports its meaning. A line range SHALL count as unsupported when no source covers
it.

#### Scenario: A Bulgarian explanation of English code (high confidence)
- **WHEN** a Bulgarian question about how the content guard withholds an excerpt is answered in Bulgarian, citing `src/Maf.Lab.Api/Agent/Guardrail.cs:153-195`, and that snippet is among the sources
- **THEN** the request uses the codebase context, and a grounding probability at or above the pass threshold yields `pass`

#### Scenario: A cited line range no source covers (low confidence)
- **WHEN** a code answer cites `src/Maf.Lab.CodeSearch/Program.cs:120-140` and no source covers those lines, and Jev's grounding probability is below the signal floor
- **THEN** the verdict is `not_grounded` and the turn carries `answer_not_grounded`

#### Scenario: A code answer Jev is unsure about (medium confidence)
- **WHEN** a code answer's grounding probability falls inside the review band
- **THEN** the verdict is `uncertain`, with no review signal

#### Scenario: A billing turn keeps its context
- **WHEN** a turn read only `search_documents` excerpts and billing records
- **THEN** the request uses the billing and portfolio context, exactly as before this change

#### Scenario: The codebase check is unavailable
- **WHEN** the answer check of a code answer times out
- **THEN** the verdict is `unchecked` with the reason, no signal is added, and the turn completes as before

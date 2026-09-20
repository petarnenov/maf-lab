# Spec Delta

## ADDED Requirements

### Requirement: The scenarios an outside client must pass are a dataset
What a client that shares no code with this system must be able to do over A2A SHALL be listed in
`evals/a2a-conformance.jsonl`, one scenario per row, and SHALL be executed by such a client rather than by the
harness — which links against this system and so could not prove the claim. The run SHALL report the share of
scenarios that passed, in the same shape the other suites report, and SHALL name every one that did not.

#### Scenario: The list is what runs
- **WHEN** a scenario is added to the dataset
- **THEN** the conformance run executes it without any other change

#### Scenario: A failure is named
- **WHEN** a scenario fails
- **THEN** the report names it and the run does not pass

#### Scenario: Run by an outsider
- **WHEN** the conformance scenarios run
- **THEN** they are driven by a client built from the agent card alone

### Requirement: A hostile verdict is a fixture, not a hope
Verdicts a broken or hostile reviewer might send SHALL be held in `evals/injection-a2a.jsonl`, and each one SHALL
be driven through the check that decides whether a verdict is believed and through the write flow. None of them
SHALL change what executes, and a verdict naming another account or adjustment SHALL be treated as a failed
review rather than as an answer.

#### Scenario: An embedded instruction
- **WHEN** a verdict's text tells the system to approve something else
- **THEN** nothing is proposed or applied for that something else

#### Scenario: A verdict about another account
- **WHEN** a verdict names an account other than the one asked about
- **THEN** it is not treated as a verdict

### Requirement: The browser is proved against runs the server produced
Runs captured from the running stack SHALL be held in `evals/ui-events.jsonl`, each with the state the browser
should end in, and replaying one SHALL produce that state. A change to what the server emits SHALL therefore be
visible as a failure in the browser's own tests.

#### Scenario: A recorded run
- **WHEN** a recorded run is replayed through the reducer
- **THEN** the answer, the tool calls, the sources and the pending write match what the recording says

#### Scenario: A run that paused
- **WHEN** the recorded run is one that paused for a confirmation
- **THEN** replaying it leaves the turn waiting on that proposal

## MODIFIED Requirements

### Requirement: JSONL datasets
The harness SHALL read datasets from `evals/`: `selection.jsonl` (question,
expectedTools — empty means no tool), `retrieval.jsonl` (query,
relevantChunkIds), `generation.jsonl` (question, reference answer, expected
source docIds), `injection.jsonl` (question, forbidden strings, forbidden
tenant ids), and `confirmation.jsonl` (a proposal and the facts its summary
must state).

`evals/` also holds datasets the harness does not read, because what executes them is not the harness: the A2A
conformance scenarios, the hostile verdicts, and the recorded runs. They live with the others because they are
the same kind of thing — a list of cases kept outside the code that checks them.

A retrieval case MAY declare the language its query is written in. A case without one SHALL be treated as the
corpus language, so existing datasets keep working unchanged.

#### Scenario: Selection coverage
- **WHEN** the selection dataset is loaded
- **THEN** it contains obvious-docs, obvious-data, boundary (two tools), and negative cases

#### Scenario: Dataset row
- **WHEN** the harness loads `retrieval.jsonl`
- **THEN** each row contributes its query, the chunk ids that should be retrieved, and the language of the query when it declares one

#### Scenario: Case without a language
- **WHEN** a row declares no language
- **THEN** it is counted as the corpus language

#### Scenario: A confirmation case
- **WHEN** the harness loads `confirmation.jsonl`
- **THEN** each row contributes the proposal to make and the facts the summary put to a person must state

#### Scenario: A dataset the harness does not read
- **WHEN** the harness loads its datasets
- **THEN** it does not require the conformance, verdict or recorded-run files to be present

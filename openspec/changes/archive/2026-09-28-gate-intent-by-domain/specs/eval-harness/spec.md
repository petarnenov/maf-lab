# Spec Delta

## ADDED Requirements

### Requirement: Intent classification is measured directly
The harness SHALL have an `intent` suite that measures the intent classifier on its own — without the answering model
and without tools — over `evals/intent.jsonl`, whose cases give a question, whether retrieval should be forced, a
category and a language. The dataset SHALL include questions outside the documented domain phrased as procedures, in
every language and script the dataset covers, because a dataset made only of in-domain questions cannot show a
classifier that forces retrieval for everything phrased as a procedure. The suite SHALL report forcing accuracy, the
share of should-not-force cases correctly left unforced, and the share of should-force cases forced — each reported
separately so a gain in one cannot hide a loss in the other — overall and per language, SHALL name every case it got
wrong, and SHALL gate against thresholds and the baseline like the other suites.

#### Scenario: Off-domain procedural question forced
- **WHEN** the classifier forces retrieval for "How do I cook carbonara?"
- **THEN** the `intent` report counts it against the share of should-not-force cases left unforced, and names the case

#### Scenario: Per language
- **WHEN** the intent eval runs over cases in English, Bulgarian and Bulgarian written in Latin letters
- **THEN** the report gives forcing accuracy for each of them as well as overall

#### Scenario: No answering model
- **WHEN** the intent eval runs
- **THEN** no chat model is called and no tool runs

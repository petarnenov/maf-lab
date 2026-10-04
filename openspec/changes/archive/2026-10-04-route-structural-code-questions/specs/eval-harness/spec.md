# Spec Delta

## ADDED Requirements

### Requirement: Code-route suite
The harness SHALL hold `evals/code-route.jsonl`. Each row SHALL have:
- a question;
- the code-route option it should get: callers, callees, impact, text or none;
- its language: en, bg or bg-latn;
- its split: design or holdout.

A row SHALL also say whether its question names an argument the fixed patterns can take. Rows labelled text or none
SHALL include questions about code and questions outside the codebase.

A `code-route` suite SHALL ask Jev the classification request alone, with no chat model and no tool call, and SHALL
report:
- accuracy of the chosen option;
- structural recall: the share of callers, callees and impact rows that code would route to the expected tool and
  direction;
- text kept: the share of text and none rows that code would not route;
- each of these per language and per split.

Rows with an extractable argument and rows without one SHALL be reported apart, so a pattern miss is not read as a Jev
error. Requests that fail SHALL be counted and reported apart from the scores. The suite SHALL show one determinate
progress bar over its rows and SHALL carry row ids and counts in its progress, never the question.

#### Scenario: A structural row routed
- **WHEN** the suite runs a callers row that names `TenantScopedSearch.QueryAsync` and Jev answers callers above the floor
- **THEN** the row counts toward structural recall

#### Scenario: A text row left alone
- **WHEN** the suite runs a text row such as "how does the code make a tool call idempotent?"
- **THEN** it counts toward text kept only if code would not route it

#### Scenario: Per language and split
- **WHEN** the dataset holds rows in en, bg and bg-latn, in both splits
- **THEN** the report gives accuracy, structural recall and text kept for each language and each split as well as overall

#### Scenario: A failed request
- **WHEN** Jev times out on a row
- **THEN** the row is counted as failed and reported apart, not as a wrong answer

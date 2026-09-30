# Spec Delta

## Purpose

Defines which remote models the test-generation agent may use, how one is chosen for a run, and what the run is
expected to cost before it starts.

## ADDED Requirements

### Requirement: Configured allowlist
The models offered for a test-generation run SHALL come only from a backend allowlist in configuration. Each entry
SHALL carry a display name, the provider tag, prices per 1M input and output tokens, a short "best for" note, and
whether it is the default. Exactly one entry SHALL be the default. The initial allowlist is `glm-5.3:cloud`
(default, recommended), `kimi-k3:cloud`, `glm-5.3-flash:cloud`, `deepseek-v4-pro:cloud` and
`deepseek-v4.1-flash:cloud`. This allowlist SHALL NOT affect the model used by the chat assistant.

#### Scenario: Listing models
- **WHEN** an administrator opens the model picker
- **THEN** it lists each allowlisted model with name, prices and note, with the default preselected

#### Scenario: Chat model untouched
- **WHEN** the allowlist is changed
- **THEN** the chat assistant still uses its own configured model

### Requirement: Availability is shown, not assumed
The picker SHALL show whether each model is currently available to the configured account. A model the provider
has refused (for example, not included in the account's plan) SHALL be shown as unavailable and SHALL NOT be
selectable. Availability SHALL be checked without sending repository content, and SHALL be reused for a short,
stated period.

#### Scenario: Model not in the plan
- **WHEN** the provider answers that `kimi-k3:cloud` is not included in the account's plan
- **THEN** it is listed as unavailable and cannot be picked

### Requirement: Exactly one model, validated server-side
A run SHALL NOT start without exactly one selected model. The server SHALL validate the model against the allowlist
and its availability at start, whatever the browser sent. The agent SHALL receive only a model that passed this
validation.

#### Scenario: Model not in allowlist
- **WHEN** a start request names `llama9:cloud`, which is not in the allowlist
- **THEN** it is rejected as invalid, no run is created and the threshold is unchanged

#### Scenario: No model selected
- **WHEN** the administrator has not selected a model
- **THEN** the Start control is disabled, and a request without a model is rejected by the server

### Requirement: Cost estimate before start
Before a run starts, the picker SHALL show an estimated cost for the selected model: the tokens expected for up to 5
attempts on this file, priced at the model's configured rates. It SHALL also show the run's cost cap. Prices that are
configured estimates SHALL be labelled as estimates.

#### Scenario: Estimate changes with the model
- **WHEN** the administrator switches from `glm-5.3:cloud` to `glm-5.3-flash:cloud`
- **THEN** the estimated cost updates to the flash model's prices

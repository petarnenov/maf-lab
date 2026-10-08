# Spec Delta

## MODIFIED Requirements

### Requirement: The out-of-scope reply names the codebase
The fixed reply to a question outside every domain SHALL say that the assistant helps with the firm's billing and
portfolios, with questions about this lab's code and with the history of Bulgaria, in the language of the question.

#### Scenario: Reply in Bulgarian
- **WHEN** a Bulgarian question is outside every domain
- **THEN** the reply, in Bulgarian, names billing, portfolios, the lab's code and the history of Bulgaria

#### Scenario: Reply in English
- **WHEN** an English question is outside every domain
- **THEN** the reply, in English, names billing, portfolios, the lab's code and the history of Bulgaria

## ADDED Requirements

### Requirement: The system prompt covers the Bulgarian history domain
The system prompt in use SHALL name the Bulgarian history domain and its tool `search_bulgarian_history`, give at least
one example question routed to it, and state in its scope that the history of Bulgaria is answered only from what the
tool returns — when the corpus does not cover a question, the answer says so rather than answering from general
knowledge. The previous prompt SHALL remain selectable by configuration for rollback.

#### Scenario: The default prompt names the domain
- **WHEN** the agent starts with no prompt version configured
- **THEN** the prompt in use names `search_bulgarian_history` and the history of Bulgaria in its tools and scope

#### Scenario: Rollback
- **WHEN** the previous prompt version is configured
- **THEN** that prompt is used and it does not name `search_bulgarian_history`

#### Scenario: A question the corpus does not cover
- **WHEN** the forced search returns no matching documentation for a history question
- **THEN** the model is told the search found nothing, and the prompt instructs it to say the documentation does not cover the question

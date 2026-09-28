# Spec Delta

## ADDED Requirements

### Requirement: Several forced calls are always issued on the model's behalf
A turn that must issue more than one call before the model's first call SHALL have those calls issued on the model's
behalf. This covers the searches of a crossing question, and a named run's status beside its search. It SHALL hold
even when required-tool-mode emulation is configured off: a provider's `tool_choice` can require only one function.

#### Scenario: Emulation off, crossing question
- **WHEN** emulation is off and a procedural question crosses billing and portfolio
- **THEN** both `search_documents` and `search_portfolio_documents` are called before the model's first call

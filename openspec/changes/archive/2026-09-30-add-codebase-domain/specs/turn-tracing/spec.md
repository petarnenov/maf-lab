# Spec Delta

## ADDED Requirements

### Requirement: The trace says which domains' tools were loaded
The `domain` event SHALL record the domains whose tools the turn loaded. It SHALL also record the reason:
- `in scope`: the classification's domains;
- `conversation`: the conversation's stored domains, for a follow-up;
- `all`: no domain verdict.

It SHALL record the conversation's stored domains before the turn.

#### Scenario: A follow-up
- **WHEN** a follow-up in no domain loads the conversation's portfolio tools
- **THEN** the `domain` event records `loaded: [portfolio]` with the reason `conversation`

# Spec Delta

## ADDED Requirements

### Requirement: Routed read call on data turns
When a turn's classification routes it to a read tool (as specified by `intent-classification`), the agent SHALL
issue that tool call with the routed arguments on the model's behalf, before and instead of the model call that would
otherwise choose it, and SHALL then let the model answer with the result in context, free to call further tools. A
routed call SHALL go through the same tool path as a call the model makes: over MCP, audited, traced, wrapped in the
data envelope, and scoped to the caller's tenant. Only read tools SHALL be routed; a write SHALL always be chosen by the
model and go through its confirmation flow. A turn that is not routed SHALL behave as it does without routing.

#### Scenario: Routed status question
- **WHEN** routing is on and the user asks "status of run 4417"
- **THEN** `get_billing_run_status` is called for run 4417 before any model call, and the turn makes one model call, which answers with the status

#### Scenario: Routed call is audited like any other
- **WHEN** a turn is routed
- **THEN** the call appears in the audit log and the trace with its arguments and result, exactly as a model-chosen call would

#### Scenario: The write stays with the model
- **WHEN** the user asks to credit or reduce an account's fee
- **THEN** no call is issued on the model's behalf, and `propose_fee_adjustment` is called only if the model chooses it

#### Scenario: Not routed
- **WHEN** a data question is not routed
- **THEN** the turn proceeds as it did before routing existed, and the model chooses the tool

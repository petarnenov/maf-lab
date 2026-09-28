# Spec Delta

## ADDED Requirements

### Requirement: The domain boundary is traced
The turn trace SHALL show where a question sits among the domains and where the turn crossed between them.
- **`domain` event:** right after `intent`, when Jev answered. It SHALL carry each domain's probability, the scope
  floor, the domains in scope, the primary domain, whether the question crosses, and the searches forced.
- **Tool events:** every `tool.forced`, `tool.call` and `tool.result` SHALL carry the `domain` and `server` of its
  tool.
- **`boundary` event:** each time a tool call's domain differs from the domain of the previous tool call in the turn,
  naming the two domains and the tool.
- **`turn.end`:** SHALL carry:
  - the turn's domain path, with consecutive repeats collapsed;
  - the domains it touched;
  - the domains Jev predicted.

#### Scenario: Crossing turn
- **WHEN** a turn calls `search_documents` and then `get_aum_history`
- **THEN** the trace has a `boundary` event from billing to portfolio, and `turn.end` has the domain path `["billing", "portfolio"]`

#### Scenario: Single-domain turn
- **WHEN** a turn calls only billing tools
- **THEN** there is no `boundary` event and the domain path is `["billing"]`

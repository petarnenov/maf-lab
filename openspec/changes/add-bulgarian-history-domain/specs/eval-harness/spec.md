# Spec Delta

## ADDED Requirements

### Requirement: Bulgarian history is measured as a domain
The domain dataset SHALL hold Bulgarian history questions in English, Bulgarian and Latin-script Bulgarian, labelled
`bulgarian-history`, and negative questions for each descriptive exclusion of the domain — an account's AUM or market
value over quarters (labelled `portfolio`), past billing runs (labelled `billing`), the user's earlier conversations
with the assistant (labelled `none`) and the code's commit history (labelled `codebase`). A domain case's label SHALL
accept `bulgarian-history` alone or joined with other domains in the trace's fixed order.

The selection dataset SHALL hold Bulgarian history cases expecting `search_bulgarian_history`, under a category of their
own. The eval host SHALL load the Bulgarian history server beside the others, in-process unless an endpoint is
configured, and `make eval` SHALL point it at the running stack's `/bulgarian-history/mcp`.

#### Scenario: History accuracy is reported
- **WHEN** the domain suite runs
- **THEN** the report gives recall over the `bulgarian-history` cases, and a negative case that lands in bulgarian-history counts as a failure naming both labels

#### Scenario: The floor sweep covers the new domain
- **WHEN** the domain suite runs
- **THEN** the floor-sweep variant re-reads every answer, the history cases included, at each candidate scope floor

#### Scenario: Selection cases load
- **WHEN** the selection dataset is loaded
- **THEN** rows expecting `search_bulgarian_history` under the `bulgarian-history` category are accepted, and an unknown tool name still fails loudly

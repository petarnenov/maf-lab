# Spec Delta

## ADDED Requirements

### Requirement: Domains view in the monitor
The monitor SHALL have a Domains view showing:
- Jev's probability for each domain against the scope floor;
- the domains in scope, and whether the question crosses the boundary;
- the path the turn took: each tool call in order, with its domain, its server instance and its latency;
- each boundary crossing between calls;
- where the prediction and the calls disagree.

When a turn touched more than one domain, the monitor header SHALL show the path as a chip (e.g. `billing → portfolio`).

#### Scenario: Crossing turn in the monitor
- **WHEN** a person opens the Domains view of a turn that crossed from billing to portfolio
- **THEN** they see both domains' probabilities, the calls grouped by domain in order, and the crossing between them

## MODIFIED Requirements

### Requirement: The api is the only A2A client
Runs SHALL be started only through the api. The api SHALL create one long-running A2A task on the test-generation
agent per run and SHALL persist the run with its task id and its budget. It SHALL follow the general rules of outbound
A2A consultation: discovery by card, the assistant's own service credentials, a deadline, and an audit record of each
operation without content. A run's deadline SHALL be long enough for the attempt cap (10 attempts) and SHALL be configurable. The run's
budget is the one the administrator chose at start: a token cap, a cost cap, both, or neither (unlimited). The api
SHALL pass exactly that budget to the task and SHALL NOT add a default cap of its own.

#### Scenario: Start
- **WHEN** an administrator starts a run with a valid model
- **THEN** the api creates the A2A task, persists the run with the task id, and returns the run as `submitted`

#### Scenario: Start with a budget
- **WHEN** an administrator starts a run with a cost cap of $0.50 and no token cap
- **THEN** the task carries a cost cap of $0.50 and no token cap, and the run's summary shows that budget

#### Scenario: Start without a budget
- **WHEN** an administrator starts a run without a budget
- **THEN** the task carries no caps and the run's summary shows the budget as unlimited

#### Scenario: Invalid budget
- **WHEN** a start request carries a token cap of 0 or a negative cost cap
- **THEN** it is rejected as invalid, no run is created and the threshold is unchanged

#### Scenario: Agent unreachable
- **WHEN** the agent's card or endpoint cannot be reached at start
- **THEN** the start fails with "agent unavailable", no active run remains, and an audit record says unreachable

#### Scenario: A long run is not cut short
- **WHEN** a run is still working in attempt 9, an hour after it started
- **THEN** it is not canceled for its deadline

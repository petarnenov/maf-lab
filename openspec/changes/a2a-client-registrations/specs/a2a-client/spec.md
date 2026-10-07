# Spec Delta

## ADDED Requirements

### Requirement: One registration shape per agent consulted
The assistant's client registration at another agent SHALL be configured as `A2A:Clients:<agent>` (client id and
secret), its endpoint defaulting from the agent's manifest or card; no section SHALL be named after one agent.

#### Scenario: The reviewer is consulted
- **WHEN** the compliance reviewer is installed and `A2A:Clients:compliance` holds the assistant's credentials
- **THEN** a consultation authenticates with them, as it did with the `Compliance` section

#### Scenario: The reviewer's live tasks
- **WHEN** the change is deployed over an existing reviewer's task store
- **THEN** its tasks are still found under the `compliance` keyspace

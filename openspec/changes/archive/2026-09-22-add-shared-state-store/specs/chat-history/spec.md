# Spec Delta

## ADDED Requirements

### Requirement: A conversation is kept for a stated period
A conversation and its messages SHALL be kept for a configured period and removed once it has passed, whether or
not anyone deleted it. That period SHALL be stated in the configuration as the compliance retention for message
content, and SHALL be independent of how long the turn traces or the logs are kept: changing one SHALL NOT change
the other.

A conversation a person deleted SHALL stop being listed and readable immediately, whatever the retention says.

#### Scenario: An old conversation
- **WHEN** a conversation's last activity is older than the retention period
- **THEN** it is removed, and asking for it returns not found

#### Scenario: Two retentions, two settings
- **WHEN** the trace retention is changed
- **THEN** how long conversations are kept is unchanged, and the reverse holds as well

#### Scenario: Deleting does not wait for retention
- **WHEN** a person deletes a conversation
- **THEN** it stops being listed and readable at once

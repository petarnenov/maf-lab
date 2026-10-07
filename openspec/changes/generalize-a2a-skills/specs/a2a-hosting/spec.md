# Spec Delta

## ADDED Requirements

### Requirement: A domain contributes its own A2A skills
The agent card's skills SHALL come from the installed domains, each contributing its own; the a2a plugin SHALL NOT name
a domain.

#### Scenario: A domain is installed
- **WHEN** a domain plugin that contributes an A2A skill is installed and a partner fetches the card
- **THEN** the card lists that skill, and a request for it is served by that domain

#### Scenario: The domain leaves
- **WHEN** that domain plugin is switched off
- **THEN** the next fetch of the card no longer lists its skills, with no restart

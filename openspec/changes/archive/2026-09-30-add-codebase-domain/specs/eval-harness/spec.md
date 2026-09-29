# Spec Delta

## ADDED Requirements

### Requirement: The codebase is measured as a domain
The domain dataset SHALL hold codebase questions in English, Bulgarian and Latin-script Bulgarian. It SHALL also hold
general-programming questions labelled `none`. A domain case's label SHALL name the domains in scope:
- one domain;
- several, joined in a fixed order, where the legacy label `both` means billing and portfolio;
- or `none`.

The selection dataset SHALL hold codebase cases expecting `search_codebase`. The eval host SHALL load the codebase
server beside the others.

#### Scenario: Codebase accuracy is reported
- **WHEN** the domain suite runs
- **THEN** the report gives accuracy over the codebase cases, and the general-programming cases count toward none-accuracy

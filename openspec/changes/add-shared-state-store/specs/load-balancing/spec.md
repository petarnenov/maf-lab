# Spec Delta

## MODIFIED Requirements

### Requirement: Cross-replica consistency
State that must outlive a single request SHALL be shared across replicas: conversations and their messages, what a
conversation is waiting on, the state of a run while it runs, the tasks and webhook registrations of every agent
this system hosts, the record of processed idempotency keys, feedback, review queue, audit and admin jobs. A
handle this system hands out SHALL be understood by whichever replica receives it back.

#### Scenario: Conversation continues on another replica
- **WHEN** two turns of one conversation are served by different api replicas
- **THEN** the second turn has the first turn in its history

#### Scenario: A run is rejoined on another replica
- **WHEN** a client loses its stream and asks about the run through a different replica
- **THEN** that replica reports the run and what it has done so far

#### Scenario: A webhook registered on one replica
- **WHEN** a webhook is registered through one replica and the state it watches changes on another
- **THEN** the delivery is made

#### Scenario: Admin job status from any replica
- **WHEN** an indexing job is started through the balancer and its status is polled repeatedly
- **THEN** every poll returns the job, whichever replica serves it, until it reports `succeeded` or `failed`

#### Scenario: No duplicate concurrent jobs
- **WHEN** a FIRM_ADMIN starts indexing twice while the first job is running
- **THEN** the second request returns the running job instead of starting another

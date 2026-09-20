# a2a-observability Specification

## Purpose
What an operator can see of the conversations this system has with other agents — what arrived, what was asked of
whom, what was delivered — and what they can stop.

## Requirements

### Requirement: What arrived from other agents is visible
An authorised person SHALL be able to see the tasks partner systems have started with this system: which partner,
which operation, the task, its state, when it started and when it last changed. Only tasks concerning the
viewer's own firm SHALL be listed, and message content MUST NOT be shown.

#### Scenario: A partner's tasks
- **WHEN** a partner has run tasks against this system
- **THEN** a FIRM_ADMIN of the firm they concerned sees each one with its partner, operation, task id, state and times

#### Scenario: Another firm's tasks
- **WHEN** tasks concerning another firm exist
- **THEN** they are not listed, whatever parameters are given

#### Scenario: No content
- **WHEN** the list is shown
- **THEN** nothing from any message a partner sent or received appears in it

### Requirement: What this system asked of another agent is visible
The same view SHALL show the consultations this system has sent: which agent, the task it concerned, the outcome
and how long it took. As with the record it is drawn from, the content of what was sent or received MUST NOT
appear.

#### Scenario: Consultations
- **WHEN** the assistant has consulted the compliance reviewer
- **THEN** each consultation is listed with the agent, the task, the outcome and the duration

#### Scenario: A consultation that failed
- **WHEN** a consultation timed out or the agent was unreachable
- **THEN** it is listed with that outcome rather than omitted

### Requirement: Push deliveries are visible
Every attempt to deliver a task's state change to a registered webhook SHALL be listed with the task, the state
it carried, how many attempts it took, whether it arrived and, when it did not, what went wrong.

#### Scenario: A delivery
- **WHEN** a task's state changes and a webhook is registered
- **THEN** the delivery appears with its task, state, attempts and whether it arrived

#### Scenario: A failed delivery
- **WHEN** a webhook could not be reached
- **THEN** the attempt is listed as not delivered, with a short reason

### Requirement: A running task can be stopped from here
An authorised person SHALL be able to cancel a task of their own firm that is still running, and the task SHALL
report itself cancelled afterwards. A task that has already finished SHALL NOT be cancellable, and a task of
another firm SHALL NOT be cancellable at all.

#### Scenario: Cancelling
- **WHEN** a FIRM_ADMIN cancels a running task of their firm
- **THEN** the task ends and is reported as cancelled

#### Scenario: Already finished
- **WHEN** the task has already completed
- **THEN** it is not cancelled and the answer says so

#### Scenario: Another firm's task
- **WHEN** a FIRM_ADMIN cancels a task concerning another firm
- **THEN** it is refused and nothing is cancelled

# a2a-observability Specification

## Purpose
What an operator can see of the conversations this system has with other agents — what arrived, what was asked of
whom, what was delivered — and what they can stop.

## Requirements

### Requirement: What arrived from other agents is visible
An authorised person SHALL be able to see the tasks partner systems have started with this system: which partner,
which operation, the task, its state, when it started and when it last changed. Only tasks concerning the
viewer's own tenant SHALL be listed, and message content MUST NOT be shown.

#### Scenario: A partner's tasks
- **WHEN** a partner has run tasks against this system
- **THEN** a TENANT_ADMIN of the tenant they concerned sees each one with its partner, operation, task id, state and times

#### Scenario: Another firm's tasks
- **WHEN** tasks concerning another tenant exist
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
An authorised person SHALL be able to cancel a task of their own tenant that is still running, and the task SHALL
report itself cancelled afterwards. A task that has already finished SHALL NOT be cancellable, and a task of
another tenant SHALL NOT be cancellable at all.

#### Scenario: Cancelling
- **WHEN** a TENANT_ADMIN cancels a running task of their tenant
- **THEN** the task ends and is reported as cancelled

#### Scenario: Already finished
- **WHEN** the task has already completed
- **THEN** it is not cancelled and the answer says so

#### Scenario: Another firm's task
- **WHEN** a TENANT_ADMIN cancels a task concerning another tenant
- **THEN** it is refused and nothing is cancelled

### Requirement: The test-generation agent is visible
A TENANT_ADMIN SHALL be able to see, read-only and through the api alone, an overview of the test-generation agent:

- whether it is configured, and whether it is reachable right now — decided by fetching its public card on the
  internal network within a short timeout — with how long that took and, when it is not reachable, a short reason
  that carries no stack trace;
- what its card says: name, description, version, its skill (id, name, description, tags), the A2A endpoint it
  advertises, the protocol version, the scope a caller needs, and whether it streams and sends push notifications;
- how the api reaches it: the configured base address and the partner id the api signs in as. The credential's
  secret MUST NOT appear;
- what a run gets by default: the default model of the allowlist, every run limit with its bounds and default
  (attempts, tool rounds per attempt, test runs per attempt, suspected bugs, deadline in minutes), and that no budget
  is applied by default;
- how many runs there are by group — running (submitted, working or verifying), candidates awaiting a decision,
  accepted, failed (failed or verification failed) and ended otherwise (discarded, canceled, no change);
- the most recent runs, newest change first and at most ten: file, state, attempt and attempt cap, the coverage
  reached and the target, the reason, the model, when it last changed, when it started, when its work ended (none
  while it is running), how long it took, the tokens and the money its model calls used, whether that money is priced
  at estimated rates, and the budget chosen at start.

How long a run took SHALL be computed by the api, in milliseconds: for a run whose work has ended, from its start to
the end of its work — so a later decision on a candidate does not lengthen it; for a running run (submitted, working
or verifying), from its start to the moment the overview is answered; and absent when the end of a stopped run is not
known.

The money a run used SHALL be the cost the run recorded, in US dollars: the agent's model calls priced at the model's
rates as they were when the run started — the same amount its cost cap is checked against — and not recomputed from
the current prices. For a running run it is what was used so far. It SHALL cover the agent's model calls only. A run
that made no model call, or whose model is priced at zero, SHALL report zero. The cost SHALL be marked as an estimate
when the run's model is priced at the lab's estimated rates, or when the model is no longer on the allowlist.

The browser SHALL NOT contact the agent: everything comes from the api. Runs describe the repository, not a firm,
so the overview SHALL NOT be filtered by any parameter, and no tenant parameter SHALL be accepted. Message content
and activity text MUST NOT appear. A card that cannot be read SHALL NOT fail the overview: the rest is still returned,
with the agent reported unreachable. Repeated requests within a few seconds SHALL reuse the last card check rather
than ask the agent again.

#### Scenario: The agent is up
- **WHEN** a TENANT_ADMIN asks for the overview while the test agent answers its card
- **THEN** it is reported reachable with the time the card took, and the card's name, version, skill, endpoint and
  required scope are shown

#### Scenario: The agent is down
- **WHEN** the configured test agent does not answer
- **THEN** the overview is still returned, the agent is reported unreachable with a short reason, no card is shown,
  and the run defaults and runs are still there

#### Scenario: No agent configured
- **WHEN** no test agent base address is configured
- **THEN** the overview says it is not configured and does not try to reach anything

#### Scenario: Run defaults
- **WHEN** the overview is shown
- **THEN** it names the default model, the attempt cap 1–10 with default 10, tool rounds 1–40 with default 40, test
  runs 0–2 with default 2, suspected bugs 0–3 with default 3, the deadline up to the configured one, and no default
  budget

#### Scenario: Runs by state
- **WHEN** there is one working run, one candidate, two accepted runs and one failed run
- **THEN** the counts read running 1, candidates 1, accepted 2, failed 1, and the recent runs list them newest change
  first with their file, state, attempt n/N, coverage and reason

#### Scenario: How long a run took
- **WHEN** a run started at 10:00 reached a candidate at 10:07 and was accepted at 11:30, and another run started
  two minutes ago is still working
- **THEN** the first is listed with its start, its end at 10:07 and a duration of 7 minutes, and the second with no
  end and a duration of about two minutes

#### Scenario: An older run whose end is unknown
- **WHEN** a stopped run has no recorded end
- **THEN** it is listed with no end and no duration, and the overview is still returned

#### Scenario: What a run cost
- **WHEN** an accepted run recorded 2 760 003 tokens costing $0.291 with a cost cap of $0.50, a working run has
  recorded $0.0421 so far, and a run that failed before any model call recorded nothing
- **THEN** the first is listed with those tokens, a cost of 0.291 and a budget of $0.50, the second with a cost of
  0.0421, and the third with a cost of 0

#### Scenario: The price the run counted, not today's
- **WHEN** a run recorded $0.29 at its model's rates, and the model's price in the allowlist has since changed
- **THEN** the run is still listed with a cost of 0.29

#### Scenario: An estimated price
- **WHEN** a run's model is on the allowlist with an estimated price, and another run's model is on it with a list
  price
- **THEN** the first run's cost is marked as an estimate and the second's is not

#### Scenario: The secret stays out
- **WHEN** the overview is returned
- **THEN** it carries the partner id the api uses but not its client secret

#### Scenario: Not an admin
- **WHEN** an advisor asks for the overview
- **THEN** it is refused

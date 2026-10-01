## MODIFIED Requirements

### Requirement: A2A screen
A FIRM_ADMIN SHALL have a screen showing what arrived from partner systems, what this system asked of another
agent, and every push delivery — each with its state and timing — and SHALL be able to cancel a task of their
firm that is still running. A person who is not a FIRM_ADMIN SHALL NOT reach it.

The screen SHALL also have a read-only section for the test-generation agent, loaded from the api on its own and
shown whether or not any partner has talked to the system: whether the agent is reachable (or not configured), its
card (name, version, description, skill, endpoint, required scope, the partner id the api signs in as), the run
defaults and limits (default model, attempts, tool rounds, test runs, suspected bugs, deadline, no default budget),
the counts of runs by group with a link to the Coverage page, and the most recent runs, each file opening on the
Coverage page. It SHALL use the screen's existing design and theme tokens in both themes.

Each recent run SHALL show how long it took, as the api reports it, in a compact form — seconds under a minute
(`42s`), minutes and two-digit seconds under an hour (`3m 05s`), hours and two-digit minutes beyond (`1h 02m`) — and
`—` when the api reports none. A running run's duration SHALL be marked as running so far and SHALL keep counting on
the page, at least once a second, from the api's value plus the time since that answer arrived, until the next
refresh replaces it.

Each recent run SHALL also show, right after its duration, what it cost as the api reports it, in US dollars: three
decimals under a dollar (`$0.042`), two from a dollar (`$1.27`), `<$0.001` for an amount above zero that would read
as `$0.000`, `$0.00` when the api reports zero, and `—` when the api reports no cost. An amount above zero that the api
marks as an estimate SHALL be prefixed with `≈` (`≈$0.042`). A running run's cost SHALL be marked as so far; it is
brought up to date by the next refresh. The cost SHALL say, on hover, the tokens used, whether the price is an
estimate, and the run's cost cap when it has one.

While the screen's data loads or is refreshed it SHALL show the app's themed progress indicator
(`role="progressbar"` with an accessible label), and the Refresh button SHALL NOT start a second refresh while one is
in flight. A section that fails to load SHALL say what could not be loaded without hiding the other.

#### Scenario: What is there
- **WHEN** a FIRM_ADMIN opens the A2A screen
- **THEN** inbound tasks, outbound consultations and push deliveries are listed, each with its state

#### Scenario: Cancelling from the screen
- **WHEN** the admin cancels a running task
- **THEN** the task is reported cancelled and the list shows it

#### Scenario: Not an admin
- **WHEN** an advisor tries to open it
- **THEN** the screen is not available to them

#### Scenario: Nothing yet
- **WHEN** no agent has talked to this system
- **THEN** the screen says so rather than showing empty tables, and the test-generation agent section is still shown

#### Scenario: The test agent section
- **WHEN** a FIRM_ADMIN opens the screen and the test agent is reachable
- **THEN** the section shows it reachable, its card, its run defaults, the run counts with a link to Coverage, and
  the recent runs with file, state, attempt n/N, coverage, reason, duration, cost and when

#### Scenario: Durations in the recent runs
- **WHEN** the api reports a finished run that took 185 000 ms, a running run at 42 000 ms, and a run with no
  duration
- **THEN** the first reads `3m 05s`, the second reads `42s` marked as so far and a second later `43s`, and the third
  reads `—`

#### Scenario: Costs in the recent runs
- **WHEN** the api reports a finished run that cost 0.291 at an estimated price with a $0.50 cost cap, a running run
  at 1.2745 at a list price, a run at 0.0004, a run at 0, and a run with no cost
- **THEN** the first reads `≈$0.291` with "of $0.50 budget" on hover, the second reads `$1.27` marked as so far, the
  third reads `<$0.001`, the fourth `$0.00`, and the fifth `—`

#### Scenario: The test agent is down
- **WHEN** the api reports the test agent unreachable
- **THEN** the section says so with the reason, and still shows the run defaults and runs

#### Scenario: Loading
- **WHEN** the screen is opened and the api has not answered yet
- **THEN** a themed progress indicator with an accessible label is shown in place of each section until it answers

#### Scenario: The overview fails
- **WHEN** the test agent overview cannot be loaded
- **THEN** the section says so, and the partner activity is still shown

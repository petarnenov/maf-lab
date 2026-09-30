## ADDED Requirements

### Requirement: The test-generation agent is visible
A FIRM_ADMIN SHALL be able to see, read-only and through the api alone, an overview of the test-generation agent:

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
  reached and the target, the reason, the model and when it last changed.

The browser SHALL NOT contact the agent: everything comes from the api. Runs describe the repository, not a firm,
so the overview SHALL NOT be filtered by any parameter, and no tenant parameter SHALL be accepted. Message content
and activity text MUST NOT appear. A card that cannot be read SHALL NOT fail the overview: the rest is still returned,
with the agent reported unreachable. Repeated requests within a few seconds SHALL reuse the last card check rather
than ask the agent again.

#### Scenario: The agent is up
- **WHEN** a FIRM_ADMIN asks for the overview while the test agent answers its card
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

#### Scenario: The secret stays out
- **WHEN** the overview is returned
- **THEN** it carries the partner id the api uses but not its client secret

#### Scenario: Not an admin
- **WHEN** an advisor asks for the overview
- **THEN** it is refused

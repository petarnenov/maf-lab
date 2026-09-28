# Spec Delta

## ADDED Requirements

### Requirement: Jev statistics per domain
The Jev statistics SHALL count judged searches per domain, taking the domain of the call that searched.

The statistics SHALL gain a Domains section, counting:
- the turns with a domain verdict;
- the turns per verdict: billing, portfolio, both or none;
- the turns whose calls crossed from one domain to the other;
- of the turns that called a tool, how many touched exactly the domains predicted.

The Domains section SHALL hold numbers only.

#### Scenario: A crossing turn
- **WHEN** a turn predicted both domains, called a billing search and a portfolio search, and the portfolio search was silenced
- **THEN** it counts once as "both", once as crossed and agreed, and the silenced search counts under portfolio

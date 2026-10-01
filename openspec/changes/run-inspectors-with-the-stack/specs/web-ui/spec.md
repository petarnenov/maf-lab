## ADDED Requirements

### Requirement: The main navigation links to the inspectors
The main navigation SHALL end, after "Curriculum", with three links: "A2A Inspector", "MCP Inspector" and "Redis
Insight", to ports 7172, 7173 and 7174 on the host the page was loaded from (so they work whether the lab is opened as
`localhost` or `127.0.0.1`). Each SHALL open in a new tab without giving the opened page access to the lab's window, and
SHALL be marked as leading outside the app (a visible external-link sign and an accessible name that says it opens in
a new tab). They SHALL be visible to every signed-in user, styled like the other navigation links in both themes, and
SHALL NOT carry any token or other state in the URL.

#### Scenario: Links present
- **WHEN** the app is open at `http://localhost:7171`
- **THEN** after "Curriculum" the navigation shows "A2A Inspector", "MCP Inspector" and "Redis Insight", pointing at
  `http://localhost:7172`, `http://localhost:7173` and `http://localhost:7174`

#### Scenario: Opened from 127.0.0.1
- **WHEN** the app is open at `http://127.0.0.1:7171`
- **THEN** the links point at `http://127.0.0.1:7172`, `:7173` and `:7174`

#### Scenario: New tab, no opener
- **WHEN** a user clicks "Redis Insight"
- **THEN** it opens in a new tab with `rel="noopener noreferrer"`, and the lab's tab stays where it was

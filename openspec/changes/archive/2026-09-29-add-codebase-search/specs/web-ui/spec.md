# Spec Delta

## ADDED Requirements

### Requirement: The right pane has Behind the scenes and Code snippets tabs
The `/chat` screen's right pane SHALL offer two tabs.
- **Behind the scenes** SHALL be selected when the page opens and SHALL show the monitor exactly as before.
- **Code snippets** SHALL show the repository files and lines that match the question of the turn the pane follows. The
  pane follows the streaming turn, or the past turn the user selected.
- Snippets SHALL be grouped per file. Each group SHALL show the file path, and each snippet its line range, symbol and
  code with its line numbers.
- Snippets SHALL be fetched only when the Code snippets tab is shown, and SHALL be fetched again when the question
  changes.
- While fetching, the tab SHALL say it is searching.
- When nothing matches, it SHALL say that no code matches the question.
- When the code search is unavailable, it SHALL say so, and the Behind the scenes tab SHALL be unaffected.
- Closing and opening the pane SHALL keep the rule that only the turn's own control closes it.

#### Scenario: Default tab
- **WHEN** a user opens `/chat`
- **THEN** the right pane shows the Behind the scenes tab with the monitor

#### Scenario: Code for the question
- **WHEN** the user asks "where is the tenant filter built?" and opens Code snippets
- **THEN** the tab lists `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs` with the matching lines, numbered

#### Scenario: Selecting another turn
- **WHEN** the Code snippets tab is open and the user selects an earlier turn
- **THEN** the tab shows the snippets for that turn's question

#### Scenario: Code search down
- **WHEN** the code snippets endpoint fails
- **THEN** the Code snippets tab shows that code search is unavailable, and switching back shows the monitor unchanged

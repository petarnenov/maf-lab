# Spec Delta

## MODIFIED Requirements

### Requirement: The right pane has Behind the scenes and Code snippets tabs
The `/chat` screen's right pane SHALL offer two tabs.
- **Behind the scenes** SHALL be selected when the page opens and SHALL show the monitor exactly as before.
- **Code snippets** SHALL show code for the turn the pane follows: the streaming turn, or the past turn the user
  selected.
  - When that turn searched the codebase, the tab SHALL show the snippets the answer used, labelled as used by the
    answer. It SHALL NOT make a request of its own for them.
  - Otherwise it SHALL show the repository files and lines that match the turn's question, labelled as related code
    that the answer did not use. These SHALL be fetched only while the tab is shown, and again when the question
    changes.
- The tab's label SHALL show how many snippets the answer used, when it used any.
- When a streaming turn's codebase search returns snippets, the pane SHALL switch to Code snippets by itself, once per
  turn. After that the user's choice of tab stands.
- Snippets SHALL be grouped per file. Each group SHALL show the file path, and each snippet its line range, symbol and
  code with its line numbers.
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

#### Scenario: The answer's own snippets
- **WHEN** the user asks "how does the code make a tool call idempotent?" and the turn searches the codebase
- **THEN** the pane switches to Code snippets, labelled with the count, showing the snippets the answer used, and no snippets request is made

#### Scenario: Related code for a turn that did not search code
- **WHEN** the user opens Code snippets on a billing turn
- **THEN** the tab fetches and shows related code, labelled as not used by the answer

#### Scenario: Selecting another turn
- **WHEN** the Code snippets tab is open and the user selects an earlier turn
- **THEN** the tab shows that turn's snippets

#### Scenario: Code search down
- **WHEN** the code snippets endpoint fails
- **THEN** the Code snippets tab shows that code search is unavailable, and switching back shows the monitor unchanged

## ADDED Requirements

### Requirement: Code sources open the Code snippets tab
In an answer's Sources, a code source SHALL read as its file name and line range, with its folder and symbol beneath.
No source SHALL widen the answer: a line too long for it SHALL be cut with an ellipsis and keep its full place as a
tooltip. An answer with more than five sources SHALL show five and offer the rest. Choosing it SHALL open the Code
snippets tab on that snippet and highlight it. The tool card of `search_codebase` SHALL read "Searching the
codebase…" while it runs and "Searched the codebase" when done. The Domains view SHALL show the codebase like the
other domains.

#### Scenario: A long symbol stays inside the answer
- **WHEN** a code source's symbol is a long test method name
- **THEN** its row is cut with an ellipsis inside the answer, the conversation gets no horizontal scroll, and the full place shows on hover

#### Scenario: Many sources
- **WHEN** an answer has 13 sources
- **THEN** it lists five and a "Show all 13" control

#### Scenario: From source to snippet
- **WHEN** the user chooses `src/Maf.Lab.Api/Agent/ToolSource.cs:17-27` in Sources
- **THEN** the right pane shows Code snippets with that snippet highlighted and scrolled into view

# Spec Delta

## MODIFIED Requirements

### Requirement: The main navigation links to the inspectors
The core navigation SHALL carry no inspector link. Each inspector plugin SHALL contribute its own navigation link
(`nav` with an `href`): "A2A Inspector", "MCP Inspector", "Redis Insight" and "Neo4j Browser", to ports 7172, 7173,
7174 and 7175 on the host the page was loaded from (so they work whether the lab is opened as `localhost` or
`127.0.0.1`), shown after the core's links while the plugin is in use. Each SHALL open in a new tab without giving the
opened page access to the lab's window, and SHALL be marked as leading outside the app (a visible external-link sign and
an accessible name that says it opens in a new tab). They SHALL be visible to every signed-in user, styled like the
other navigation links in both themes, and SHALL NOT carry any token or other state in the URL.

#### Scenario: Links present
- **WHEN** the app is open at `http://localhost:7171` with the four inspector plugins in use
- **THEN** the navigation shows "A2A Inspector", "MCP Inspector", "Redis Insight" and "Neo4j Browser", pointing at
  `http://localhost:7172`, `http://localhost:7173`, `http://localhost:7174` and `http://localhost:7175`

#### Scenario: Opened from 127.0.0.1
- **WHEN** the app is open at `http://127.0.0.1:7171`
- **THEN** the links point at `http://127.0.0.1:7172`, `:7173`, `:7174` and `:7175`

#### Scenario: New tab, no opener
- **WHEN** a user clicks "Redis Insight"
- **THEN** it opens in a new tab with `rel="noopener noreferrer"`, and the lab's tab stays where it was

#### Scenario: Without the plugins
- **WHEN** no inspector plugin is in use
- **THEN** the navigation shows no inspector link

### Requirement: Two-pane chat with behind-the-scenes monitor
The `/chat` screen SHALL show the conversation in a left pane and, while any plugin in use contributes a chat pane, a
side pane on the right holding those panes as tabs, the first one showing until the user or a plugin picks another.
With no such plugin there SHALL be no side pane and the conversation SHALL take the whole width. On screens narrower
than 1024 px the side pane SHALL stack below the conversation. The side pane SHALL follow the turn that is streaming,
and SHALL switch to a past assistant turn when the user selects it.

While the monitor plugin is in use, its "Behind the scenes" pane SHALL show the selected turn's trace: live while the
turn runs, read from the monitor's trace API only while the pane is showing and once more when the run has ended, and
the turn's stored trace once it has. A turn whose trace the monitor no longer keeps SHALL say so.

The side pane SHALL be a panel the user can close and open again. Each assistant turn SHALL carry a control, named
after the pane it opens, that says whether the side pane is showing that turn; pressing it on the turn being shown
SHALL close the side pane, and pressing it again SHALL open it on that turn. While the side pane is closed the
conversation SHALL take the room it leaves. Selecting a turn any other way SHALL only show it — nothing but that
control SHALL close the side pane, because closing it by clicking the answer being read would be a surprise.

#### Scenario: Layout
- **WHEN** a user opens `/chat` on a wide screen with the monitor plugin in use
- **THEN** the chat is on the left and the monitor on the right

#### Scenario: Without a pane
- **WHEN** no plugin in use contributes a chat pane
- **THEN** the chat has no side pane and its turns carry no control to open one

#### Scenario: Select a past turn
- **WHEN** the user clicks an earlier assistant turn with the monitor plugin in use
- **THEN** the monitor loads and shows that turn's stored trace

#### Scenario: A trace no longer kept
- **WHEN** the user selects a turn whose stored trace the monitor has dropped
- **THEN** the monitor says the trace has expired

#### Scenario: Closing the monitor
- **WHEN** the user presses the control on the turn the side pane is showing
- **THEN** the side pane closes and the turn is no longer marked as shown

#### Scenario: Opening it again
- **WHEN** the user presses that control again
- **THEN** the side pane opens on that turn

#### Scenario: Reading an answer does not close it
- **WHEN** the user clicks the body of the turn the side pane is showing
- **THEN** the side pane stays open

### Requirement: Trace from the review queue
The `/admin/feedback` review form SHALL offer the reviewing TENANT_ADMIN each review panel the plugins in use
contribute, as a control that opens and hides it for the selected turn. While the monitor plugin is in use, one of
them SHALL be the turn's trace.

#### Scenario: Reviewer opens trace
- **WHEN** a TENANT_ADMIN opens a flagged turn in the review queue with the monitor plugin in use
- **THEN** a panel shows that turn's trace

#### Scenario: Without the monitor
- **WHEN** no plugin in use contributes a review panel
- **THEN** the review form offers no panel, and labelling the turn works as before

### Requirement: The model's reasoning in the chat
When a turn's model reasons before it answers, the assistant's turn SHALL show that reasoning in the chat, set
apart from the answer and marked as the model's thinking rather than as something said to the user.

While the model is reasoning the block SHALL be open and SHALL grow as the reasoning streams. It SHALL close by
itself when the answer's first text arrives, leaving a summary that says how long the model thought and that
reopens the reasoning when it is used. Once closed by the answer, it SHALL NOT reopen by itself, and a person who
opened or closed it SHALL keep that choice for the rest of the turn.

A turn whose model did not reason SHALL show no such block. A turn being restored from history SHALL show the
reasoning the conversation kept with it, whatever plugins are in use.

#### Scenario: Reasoning while the model thinks
- **WHEN** a turn's model streams its reasoning
- **THEN** the assistant's turn shows an open block that grows with it, and the answer has not started

#### Scenario: The answer closes it
- **WHEN** the first text of the answer arrives
- **THEN** the block closes itself and shows how long the model thought, and opening it shows the reasoning again

#### Scenario: A person's choice is kept
- **WHEN** a person opens the block after it closed itself
- **THEN** it stays open for the rest of the turn

#### Scenario: A turn with no reasoning
- **WHEN** a model answers without reasoning
- **THEN** the turn shows no reasoning block

#### Scenario: A reopened turn
- **WHEN** a user opens a stored conversation whose turn kept its reasoning
- **THEN** that turn shows the reasoning, collapsed, with how long the model thought

### Requirement: Telemetry screen
The `/telemetry` screen SHALL show what the stack has measured about itself, over a period the user chooses, and
SHALL be reachable from the main navigation.

It SHALL show: how many runs and turns were started and how many failed; how long a turn takes; how long a model
call takes and how many tokens it used, by model; tool calls by tool and outcome; how long retrieval takes, split
into its stages; and how those numbers are spread across the instances that served them. Each number SHALL say
which period it covers, and a number the stack has not measured yet SHALL read as no data rather than as zero.

The screen SHALL offer a way to open a turn's trace where the spans are kept, and, while the monitor plugin is in use,
a turn on the chat screen SHALL offer the same for its own trace from the monitor. When the metrics cannot be read,
the screen SHALL say so and stay usable rather than showing an empty chart.

No message content SHALL appear on the screen, because none of it is in the signals it reads.

#### Scenario: The stack's own numbers
- **WHEN** a signed-in user opens `/telemetry` after turns have run
- **THEN** the screen shows the turn and model numbers for the chosen period, each labelled with that period

#### Scenario: Nothing measured yet
- **WHEN** no turn has run in the chosen period
- **THEN** the screen says there is no data for it rather than showing zeros as a result

#### Scenario: Per instance
- **WHEN** two api replicas have served turns
- **THEN** the screen shows how the turns were spread between them

#### Scenario: From a turn to its trace
- **WHEN** a user opens the trace of a turn from the monitor on the chat screen
- **THEN** that turn's spans open where the traces are kept

#### Scenario: Metrics unavailable
- **WHEN** the metrics store cannot be reached
- **THEN** the screen says the numbers are unavailable and still renders

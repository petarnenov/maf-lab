# Spec Delta

## MODIFIED Requirements

### Requirement: Two-pane chat with behind-the-scenes monitor
The `/chat` screen SHALL show the conversation in a left pane and a behind-the-scenes monitor in a right pane. On
screens narrower than 1024 px the monitor SHALL stack below the conversation. The monitor SHALL follow the turn
that is streaming, and SHALL switch to a past assistant turn when the user selects it.

The monitor SHALL be a panel the user can close and open again. Each assistant turn SHALL carry a control that
says whether the monitor is showing that turn; pressing it on the turn being shown SHALL close the monitor, and
pressing it again SHALL open the monitor on that turn. While the monitor is closed the conversation SHALL take
the room it leaves. Selecting a turn any other way SHALL only show it — nothing but that control SHALL close the
monitor, because closing it by clicking the answer being read would be a surprise.

#### Scenario: Layout
- **WHEN** a user opens `/chat` on a wide screen
- **THEN** the chat is on the left and the monitor on the right

#### Scenario: Select a past turn
- **WHEN** the user clicks an earlier assistant turn
- **THEN** the monitor loads and shows that turn's stored trace

#### Scenario: Closing the monitor
- **WHEN** the user presses the control on the turn the monitor is showing
- **THEN** the monitor closes and the turn is no longer marked as shown

#### Scenario: Opening it again
- **WHEN** the user presses that control again
- **THEN** the monitor opens on that turn and shows its trace

#### Scenario: Reading an answer does not close it
- **WHEN** the user clicks the body of the turn the monitor is showing
- **THEN** the monitor stays open

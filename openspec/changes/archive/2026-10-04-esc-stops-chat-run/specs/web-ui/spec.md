# Spec Delta

## ADDED Requirements

### Requirement: Esc stops the answer in progress
On the chat screen, pressing Esc SHALL stop the run in progress, whether it answers a message or follows an approval
or a rejection. The stop SHALL be asked for only through CopilotKit's own stop for the run's thread and run, and what
the stop did SHALL be shown only from the run's own AG-UI events as CopilotKit delivers them (agui-protocol-only). No
endpoint, event or request of this system's own SHALL be added for it, and the screen SHALL NOT decide by itself
that a run has stopped.

**When Esc stops a run:**
- Esc SHALL stop the run wherever the focus is on the chat screen, and only while a run is in progress. With no run in
  progress Esc SHALL do nothing on this screen.
- An Esc that a control on the page has already handled for itself (for example cancelling a rename in the history
  sidebar) SHALL NOT stop the run. An Esc pressed while an input method is composing SHALL NOT stop the run.
- Esc SHALL stop runs only on the chat screen.

**While the stop is on its way:**
- Once Esc is pressed, the turn SHALL say that it is stopping, beside the run's progress and in the page's theme,
  until the run's terminal event arrives. Pressing Esc again SHALL NOT ask for a second stop.
- Events the run still sends until then SHALL reach that turn as any run's events do.

**The stopped turn:**
- A run SHALL be shown as stopped when its terminal event is a `RUN_ERROR` with `code` `abort`, or a `RUN_FINISHED`
  whose outcome is `cancelled`.
- The turn SHALL keep what it had shown before the stop: text, reasoning, tool cards and sources. It SHALL say that it
  was stopped, its progress SHALL go away, and the stop SHALL NOT be shown as an error: no error face, no error text.
- A tool result whose content reports the status `stopped` SHALL close its tool card as stopped, not as failed; a
  tool card the stopped run left running SHALL show as stopped too.
- The composer SHALL be ready for the next message once the terminal event has arrived, and that message SHALL
  continue the same conversation.

**Discoverability:**
- While a run is in progress, its turn SHALL show that Esc stops it, beside the run's progress and in the page's theme.

#### Scenario: Stopping an answer part-way
- **WHEN** an answer has streamed "The fee schedule", the person presses Esc, and the run then ends with a `RUN_ERROR`
  whose `code` is `abort`
- **THEN** CopilotKit's stop request for that thread and run is sent, the turn still shows "The fee schedule", says
  it was stopped, shows no progress and no error, and the Send button is available again

#### Scenario: Stopped with a cancelled outcome
- **WHEN** the person presses Esc and the run ends with a `RUN_FINISHED` whose outcome is `cancelled`
- **THEN** the turn says it was stopped, with no error

#### Scenario: Stopping until the run says so
- **WHEN** the person presses Esc and the run's terminal event has not arrived yet
- **THEN** the turn says it is stopping, nothing says it was stopped, a second Esc sends no second stop, and text the
  run still streams appears in that turn

#### Scenario: Esc from outside the composer
- **WHEN** a run is in progress, the focus is on the monitor panel, and the person presses Esc
- **THEN** the stop is asked for

#### Scenario: A tool the stop interrupted
- **WHEN** a tool call is running, the person presses Esc, and the run reports that call's result with the status
  `stopped` before it ends
- **THEN** its card shows as stopped, not running and not failed

#### Scenario: A tool the run left running
- **WHEN** a tool call is running, the person presses Esc, and the run ends with a `RUN_ERROR` whose `code` is `abort`
  before reporting that call's result
- **THEN** its card shows as stopped, not running and not failed

#### Scenario: Nothing in progress
- **WHEN** no run is in progress and the person presses Esc
- **THEN** no stop is asked for and the turns on screen are unchanged

#### Scenario: A control's own Esc
- **WHEN** a run is in progress and the person presses Esc to cancel renaming a conversation in the history sidebar
- **THEN** the rename is cancelled, no stop is asked for, and the run continues

#### Scenario: Sending after a stop
- **WHEN** a run has stopped and the person sends another message
- **THEN** the message is sent in the same conversation and its answer streams into a new turn

#### Scenario: The hint while running
- **WHEN** a run is in progress
- **THEN** its turn shows "Esc to stop" beside the progress, and the hint is gone once the run has ended or stopped

## ADDED Requirements

### Requirement: Run activity view
Whenever the selected file has a run, the file region SHALL show an **Activity** button: next to the threshold's
Save, in its place while a run is active, and next to the run status a non-admin sees. The button SHALL open a modal
dialog with:
- a header with the file, target, model, state, attempt and phase, latest coverage, tokens, cost and elapsed time;
- a timeline of the run's activity: phases, tool calls with their outcome, attempt results, and the model's text
  and reasoning as collapsible blocks.

The modal, like every run status on the screen, SHALL learn about the run only from the run's AG-UI stream. While
the run is active, the header and the timeline SHALL update live, without a reload. The timeline SHALL keep
the newest entry in view unless the user has scrolled up, and SHALL offer a way back to the newest entry. For a run
that has ended, the modal SHALL show its full recorded activity and its final state. An administrator SHALL be able
to cancel an active run from the modal. The dialog SHALL be keyboard accessible: it takes focus when it opens,
closes with Escape, and returns focus to the button. Closing it SHALL NOT affect the run.

#### Scenario: Watching a run
- **WHEN** an administrator starts a run and opens Activity
- **THEN** the modal shows attempt 1 generating, then the tool calls and the model's text as they happen, without a reload

#### Scenario: Scrolled up
- **WHEN** the user scrolls up in the timeline while new entries arrive
- **THEN** the view stays where the user left it and offers a way back to the newest entry

#### Scenario: A finished run
- **WHEN** a user opens Activity for a file whose last run ended `failed`
- **THEN** the modal shows the run's whole timeline and the reason it failed

#### Scenario: A non-admin watches
- **WHEN** a user who is not an administrator opens Activity during a run
- **THEN** the modal updates live and offers no cancel

#### Scenario: No run
- **WHEN** a file has never had a run
- **THEN** no Activity button is shown

### Requirement: Refresh state is explained
The Coverage screen SHALL show the last refresh's outcome only when it did not succeed and no later refresh has
succeeded. It SHALL then say when the refresh ended and why, in user-facing words (interrupted because the service
stopped, coverage runner unavailable, no report produced, `main` has no commit), and when the latest measurement was
taken. It SHALL NOT show internal detail.

#### Scenario: Interrupted by a restart
- **WHEN** the stack stops while a refresh runs, and the screen is opened after it restarts
- **THEN** the screen says the last refresh was interrupted at that time because the service stopped, and shows when the current coverage was measured

#### Scenario: A later refresh succeeds
- **WHEN** a refresh succeeds after a failed one
- **THEN** no refresh failure is shown

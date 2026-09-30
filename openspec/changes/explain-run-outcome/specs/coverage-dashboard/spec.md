## MODIFIED Requirements

### Requirement: Screen follows run progress live
While a run is active for a file, the screen SHALL show its state and progress (attempt n of 5, latest coverage) as
updates arrive, without a page reload. This SHALL hold for the file's row in the tree as well as for the file view.
The row of a file with an active run SHALL show a live marker, a moving dot with the state, the attempt n/N and the
phase, and the marker SHALL follow the run's AG-UI stream. When a run ends, the tree and the file's detail SHALL
refresh, so the new state is shown without a manual refresh. After a run ends without a candidate, the file's row
SHALL show a short label of that outcome ("no change", "failed", "canceled", "verification failed"), with the reason
in its tooltip, until the file is measured again after the run.

#### Scenario: Progress arrives
- **WHEN** the agent finishes attempt 2 of 5 at 71%
- **THEN** the file's run status shows attempt 2/5 and 71% within a few seconds

#### Scenario: Row shows a live run
- **WHEN** a run on a file enters attempt 2 building
- **THEN** that file's row in the tree shows the live marker with "working 2/5 · building" within a few seconds, without a reload

#### Scenario: Row shows the last outcome
- **WHEN** a run ends `completed_no_change` with reason `budget`
- **THEN** the live marker is gone, and the row shows "no change" with a tooltip saying the budget would have been exceeded

#### Scenario: Run ends
- **WHEN** a run is verified and its candidate is recorded
- **THEN** the tree and file view show the candidate without the user reloading

### Requirement: Run activity view
Whenever the selected file has a run, the file region SHALL show an **Activity** button: next to the threshold's
Save, in its place while a run is active, and next to the run status a non-admin sees. The button SHALL open a modal
dialog with:
- a header with the file, target, model, budget ("unlimited" when none), state, attempt and phase, latest coverage,
  tokens, cost and elapsed time; for a run that has ended, the state SHALL be followed by why it stopped, in
  user-facing words;
- a timeline of the run's activity: phases, tool calls with their outcome, attempt results, and the model's text
  and reasoning as collapsible blocks. When the agent has stopped, the timeline SHALL end with a closing entry that
  says why ("Reached the target", "Used all 5 attempts", "Stopped before attempt 3: the budget would be exceeded").

The modal, like every run status on the screen, SHALL learn about the run only from the run's AG-UI stream. While
the run is active, the header and the timeline SHALL update live, without a reload, and the header SHALL show that
the agent is working. The timeline SHALL keep the newest entry in view unless the user has scrolled up, and SHALL
offer a way back to the newest entry. For a run that has ended, the modal SHALL show its full recorded activity and
its final state. An administrator SHALL be able to cancel an active run from the modal. The dialog SHALL be keyboard
accessible: it takes focus when it opens, closes with Escape, and returns focus to the button. Closing it SHALL NOT
affect the run.

#### Scenario: Watching a run
- **WHEN** an administrator starts a run and opens Activity
- **THEN** the modal shows attempt 1 generating, then the tool calls and the model's text as they happen, without a reload

#### Scenario: Scrolled up
- **WHEN** the user scrolls up in the timeline while new entries arrive
- **THEN** the view stays where the user left it and offers a way back to the newest entry

#### Scenario: A finished run
- **WHEN** a user opens Activity for a file whose last run ended `failed`
- **THEN** the modal shows the run's whole timeline and the reason it failed

#### Scenario: A run stopped by its budget
- **WHEN** a user opens Activity for a run that ended `completed_no_change` because the budget would be exceeded before attempt 3
- **THEN** the header shows "No change · budget", and the timeline's last entry says it stopped before attempt 3 because the budget would be exceeded

#### Scenario: A non-admin watches
- **WHEN** a user who is not an administrator opens Activity during a run
- **THEN** the modal updates live and offers no cancel

#### Scenario: No run
- **WHEN** a file has never had a run
- **THEN** no Activity button is shown

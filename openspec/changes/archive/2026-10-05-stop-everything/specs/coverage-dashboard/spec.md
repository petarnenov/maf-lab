# Spec Delta

## MODIFIED Requirements

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

An attempt result SHALL say what the attempt ran when its entry records it. A related run SHALL read as "related:
3 files, 58 tests". A whole-suite confirmation SHALL follow it as "→ whole suite: 1219 tests (confirmation)". A
whole-suite run SHALL read as "whole suite", followed by the reason when it ran instead of the related tests. A
result the runner reused SHALL be marked reused. An attempt recorded without this SHALL read as before.

The modal, like every run status on the screen, SHALL learn about the run only from the run's AG-UI stream. While
the run is active, the header and the timeline SHALL update live, without a reload, and the header SHALL show that
the agent is working. The timeline SHALL keep the newest entry in view unless the user has scrolled up, and SHALL
offer a way back to the newest entry. For a run that has ended, the modal SHALL show its full recorded activity and
its final state. An administrator SHALL be able to cancel an active run from the modal. The dialog SHALL be keyboard
accessible: it takes focus when it opens and returns focus to the button. Escape SHALL stop the active run when the
person may cancel it (stop-anything) — the modal then says "Stopping…" until the run reports canceled — and SHALL
close the dialog otherwise. The dialog's close button SHALL close it without affecting the run.

#### Scenario: Watching a run
- **WHEN** an administrator starts a run and opens Activity
- **THEN** the modal shows attempt 1 generating, then the tool calls and the model's text as they happen, without a reload

#### Scenario: An attempt confirmed on the whole suite
- **WHEN** attempt 2 ran 58 tests from 3 related files and its whole-suite confirmation ran 1219 tests
- **THEN** its timeline row reads "related: 3 files, 58 tests → whole suite: 1219 tests (confirmation)"

#### Scenario: An attempt that ran the whole suite instead
- **WHEN** attempt 1 ran the whole suite because nothing related was selected
- **THEN** its timeline row reads "whole suite: " followed by that reason

#### Scenario: An attempt recorded before scopes
- **WHEN** a user opens Activity for a run recorded before attempts carried their scope
- **THEN** its attempt rows show coverage, build and test counts as before, with no scope

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

#### Scenario: Esc stops the run for an administrator
- **WHEN** an administrator has Activity open on an active run and presses Escape
- **THEN** the run is cancelled, the modal says "Stopping…", and then shows the run canceled

#### Scenario: Esc closes the dialog for a viewer
- **WHEN** a user who may not cancel has Activity open on an active run and presses Escape
- **THEN** the dialog closes and the run goes on

## ADDED Requirements

### Requirement: The coverage refresh can be stopped
While a coverage refresh runs, an administrator SHALL be able to stop it with Esc on the coverage screen; the stop
SHALL cancel the refresh job and the coverage runner job it is waiting on, and the screen SHALL say "Stopping…" and
then show the refresh canceled, keeping the last complete coverage.

#### Scenario: Refresh stopped
- **WHEN** an administrator starts a coverage refresh and presses Esc while the runner is building
- **THEN** the refresh ends canceled, the runner job is cancelled, and the tree shows the previous coverage

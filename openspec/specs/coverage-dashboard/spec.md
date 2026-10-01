# coverage-dashboard Specification

## Purpose
Lets a signed-in user see how well the repository's own C# and TypeScript source is covered by tests: file by file,
folder by folder, and line by line, against each file's threshold.

## Requirements

### Requirement: Coverage screen in the main navigation
The web app SHALL offer a Coverage screen at `/coverage`, linked from the main navigation. Any signed-in user can
reach it, following the precedent of the Telemetry screen. The actions on it that change state (changing a
threshold, starting, cancelling, accepting or discarding a run, refreshing coverage) SHALL be offered only to a firm
administrator. The server SHALL reject those actions from anyone else, whatever the UI shows.

#### Scenario: Reaching the screen
- **WHEN** a signed-in user picks Coverage in the main navigation
- **THEN** the Coverage screen opens at `/coverage`

#### Scenario: Not signed in
- **WHEN** the coverage data is requested without a valid token
- **THEN** the request is rejected as unauthorized

#### Scenario: A non-admin sees but cannot act
- **WHEN** a signed-in user who is not a firm administrator opens the screen
- **THEN** coverage, thresholds and run status are visible, and no control that changes state is offered

### Requirement: Source tree with coverage
The screen SHALL show the repository's source files covered by the latest coverage snapshot as a tree, covering
backend C# and frontend TS/TSX. Each file SHALL show its line coverage percentage and its effective threshold. Each
folder SHALL show coverage aggregated over the lines of every file under it, weighted by line count rather than
averaged per file. A file whose coverage is below its effective threshold SHALL be visibly flagged. The flag SHALL NOT
rely on colour alone, and all colours SHALL come from the theme. A file that has a *candidate* coverage from a run
awaiting acceptance SHALL show the candidate value alongside the current one, marked as candidate.

A candidate measurement covers the whole project, but a run is judged only by the file it is for: each run awaiting
acceptance SHALL contribute the candidate value of its own file only, taken from its newest candidate measurement.
The tree SHALL load however many runs await acceptance at once.

#### Scenario: Folder aggregate is line-weighted
- **WHEN** a folder holds a 10-line file at 100% and a 90-line file at 0%
- **THEN** the folder shows 10%, not 50%

#### Scenario: File below threshold
- **WHEN** a file has 62% coverage and an effective threshold of 80%
- **THEN** the file is flagged as below threshold with a marker that is not just a colour

#### Scenario: Candidate awaiting acceptance
- **WHEN** a verified run raised a file to 86% on a candidate branch that has not been accepted
- **THEN** the file shows its current coverage and, marked as candidate, 86%

#### Scenario: Several candidates at once
- **WHEN** two runs await acceptance at once, one for `Small.cs` and one for `Large.cs`, and each run's candidate
  measurement covers both files
- **THEN** the tree loads, `Small.cs` shows the first run's candidate value for it and `Large.cs` shows the second
  run's, each marked with its own run

### Requirement: Sorting and filtering
The tree SHALL be sortable by name and by coverage, in both directions. It SHALL be filterable by a text match on the
path. It SHALL offer a "below threshold only" toggle that shows only flagged files and the folders containing them.

#### Scenario: Below threshold only
- **WHEN** the user turns on "below threshold only"
- **THEN** only files under their threshold remain, with their parent folders

#### Scenario: Sort by coverage
- **WHEN** the user sorts by coverage ascending
- **THEN** siblings in each folder are ordered from least to most covered

### Requirement: File view with line status
Selecting a file SHALL show its full source as of the snapshot's commit. Each line SHALL be marked `covered`
(executed, all branches taken), `uncovered` (executable, not executed), `partial` (executed, with some branches
not taken) or not executable (unmarked). Hovering or focusing a marked line SHALL show its hit count and, for a
partial line, how many branches were taken out of how many. A summary header SHALL show lines covered and total,
branches covered and total, line percentage, effective threshold, when the snapshot was taken, and the source commit.

#### Scenario: Partial line
- **WHEN** a line with an `if` was executed but only its true branch was taken
- **THEN** the line is marked partial and its tooltip shows 1 of 2 branches and its hit count

#### Scenario: Summary header
- **WHEN** a file is opened
- **THEN** the header shows lines, branches, %, threshold, snapshot time and the short commit SHA

### Requirement: Large files stay responsive
The file view SHALL render only the lines near the viewport, so opening or scrolling a file of several thousand
lines does not freeze the page.

#### Scenario: A 5 000-line file
- **WHEN** a file of 5 000 lines is opened
- **THEN** the page stays interactive and the number of line elements in the document stays bounded by the
  viewport, not the file length

### Requirement: Loading, empty and error states
Every region of the screen that fetches data SHALL show a loading state while waiting. When no coverage snapshot
exists yet, the screen SHALL say "No coverage report yet" and, for an administrator, offer Refresh coverage. An error
SHALL show a user-facing message without internal detail (no exception types, hosts or paths outside the repo), and
SHALL stay inside the region that failed. When a file's latest measurement names a commit the repository does not
have, the file detail SHALL answer `409` with the problem type `source_unavailable` and that commit, never `404`. The
file region SHALL then say the file was measured at a commit this repository does not have, show the short commit,
and ask for a coverage refresh. For an administrator, the region SHALL offer Refresh coverage.

#### Scenario: No snapshot yet
- **WHEN** the screen opens and nothing has been ingested
- **THEN** it shows "No coverage report yet" instead of an empty tree

#### Scenario: File view fails
- **WHEN** loading one file's detail fails for any reason other than an unavailable commit
- **THEN** the file region shows an error and the tree remains usable

#### Scenario: Measured at a commit the repository does not have
- **WHEN** a file is selected whose latest official snapshot names a commit that `git` cannot find in the repository
- **THEN** the detail request answers `409 source_unavailable` with that commit, the file region explains it with the short commit and asks for a refresh, and the tree remains usable

#### Scenario: Refresh heals an unavailable commit
- **WHEN** an administrator refreshes coverage after that message
- **THEN** the file's newest official snapshot is at `main` and the file view loads its lines

#### Scenario: Unknown path is still not found
- **WHEN** the detail is requested for a path no snapshot has
- **THEN** it answers `404`, as before

### Requirement: Screen follows run progress live
While a run is active for a file, the screen SHALL show its state and progress (attempt n of N, where N is the run's attempt cap, and the latest coverage) as
updates arrive, without a page reload. This SHALL hold for the file's row in the tree as well as for the file view.
The row of a file with an active run SHALL show a live marker, a moving dot with the state, the attempt n/N and the
phase, and the marker SHALL follow the run's AG-UI stream. When a run ends, the tree and the file's detail SHALL
refresh, so the new state is shown without a manual refresh. After a run ends without a candidate, the file's row
SHALL show a short label of that outcome ("no change", "failed", "canceled", "verification failed"), with the reason
in its tooltip, until the file is measured again after the run.

#### Scenario: Progress arrives
- **WHEN** the agent finishes attempt 2 of 10 at 71%
- **THEN** the file's run status shows attempt 2/10 and 71% within a few seconds

#### Scenario: Row shows a live run
- **WHEN** a run on a file enters attempt 2 building
- **THEN** that file's row in the tree shows the live marker with "working 2/10 · building" within a few seconds, without a reload

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

An attempt result SHALL say what the attempt ran when its entry records it. A related run SHALL read as "related:
3 files, 58 tests". A whole-suite confirmation SHALL follow it as "→ whole suite: 1219 tests (confirmation)". A
whole-suite run SHALL read as "whole suite", followed by the reason when it ran instead of the related tests. A
result the runner reused SHALL be marked reused. An attempt recorded without this SHALL read as before.

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

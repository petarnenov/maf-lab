# Spec Delta

## Purpose

Lets a signed-in user see how well the repository's own C# and TypeScript source is covered by tests: file by file,
folder by folder, and line by line, against each file's threshold.

## ADDED Requirements

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

#### Scenario: Folder aggregate is line-weighted
- **WHEN** a folder holds a 10-line file at 100% and a 90-line file at 0%
- **THEN** the folder shows 10%, not 50%

#### Scenario: File below threshold
- **WHEN** a file has 62% coverage and an effective threshold of 80%
- **THEN** the file is flagged as below threshold with a marker that is not just a colour

#### Scenario: Candidate awaiting acceptance
- **WHEN** a verified run raised a file to 86% on a candidate branch that has not been accepted
- **THEN** the file shows its current coverage and, marked as candidate, 86%

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
SHALL stay inside the region that failed.

#### Scenario: No snapshot yet
- **WHEN** the screen opens and nothing has been ingested
- **THEN** it shows "No coverage report yet" instead of an empty tree

#### Scenario: File view fails
- **WHEN** loading one file's detail fails
- **THEN** the file region shows an error and the tree remains usable

### Requirement: Screen follows run progress live
While a run is active for a file, the screen SHALL show its state and progress (attempt n of 5, latest coverage) as
updates arrive, without a page reload. When a run ends, the tree and the file's detail SHALL refresh, so the new
state is shown without a manual refresh.

#### Scenario: Progress arrives
- **WHEN** the agent finishes attempt 2 of 5 at 71%
- **THEN** the file's run status shows attempt 2/5 and 71% within a few seconds

#### Scenario: Run ends
- **WHEN** a run is verified and its candidate is recorded
- **THEN** the tree and file view show the candidate without the user reloading

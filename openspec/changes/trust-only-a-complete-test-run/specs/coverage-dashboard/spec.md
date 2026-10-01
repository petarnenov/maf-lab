## MODIFIED Requirements

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

## MODIFIED Requirements

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

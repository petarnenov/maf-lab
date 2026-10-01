## MODIFIED Requirements

### Requirement: The documentation check
`make docs-check` SHALL change no file and SHALL exit non-zero, naming the file, the line where one exists, and the
reason, when any of the following holds:
- a generated block differs from what `make docs` would write, or its markers are missing or unbalanced;
- a route the api registers (method and path) has no row in `docs/http-api.md`, or a row there names a route the api
  does not register;
- a document references `make <target>` for a target the Makefile does not define;
- a project under `src/` or `tools/`, or a top-level directory tracked by git, has no description;
- a document names a chat, embedding or Jev model version that differs from the value the code configures;
- a relative link or image in a checked document points to a file that does not exist;
- a page the web app registers is not named in `README.md` as its path in code format (`` `/path` ``), where a page is
  a route with its own screen — redirects and the catch-all are not pages, and a route's optional or parameter
  segments are not part of the name it must have;
- an active change's proposal has no `Documentation impact` section.
The checked documents SHALL be `README.md`, `CLAUDE.md`, `docs/**/*.md`, `openspec/project.md` and
`.github/copilot-instructions.md`. `DECISIONS.md`, eval reports, prompts and archived changes are history, and SHALL
NOT be checked for model names or routes. It SHALL exit zero when none holds.

#### Scenario: Clean tree passes
- **WHEN** `make docs-check` runs on a tree where `make docs` has been run and every rule holds
- **THEN** it exits zero and prints a one-line summary of what it checked

#### Scenario: Hand-edited generated block
- **WHEN** someone edits a line inside a generated block by hand
- **THEN** `make docs-check` fails, names the block and file, and says to run `make docs`

#### Scenario: Endpoint without documentation
- **WHEN** a new api endpoint is registered and `docs/http-api.md` has no row for its method and path
- **THEN** `make docs-check` fails and names the method and path

#### Scenario: Documentation for a removed endpoint
- **WHEN** `docs/http-api.md` has a row for a route the api no longer registers
- **THEN** `make docs-check` fails and names the row

#### Scenario: Stale model name
- **WHEN** a checked document names an embedding model other than the one the default embedding profile configures
- **THEN** `make docs-check` fails with the file, line, found name and configured name

#### Scenario: History is not checked
- **WHEN** `DECISIONS.md` or an archived change names a model that is no longer configured
- **THEN** `make docs-check` does not fail on it

#### Scenario: Broken link
- **WHEN** a checked document links to a relative path that does not exist
- **THEN** `make docs-check` fails and names the link

#### Scenario: Unknown make target
- **WHEN** a checked document tells the reader to run `make <x>` and the Makefile has no target `x`
- **THEN** `make docs-check` fails and names the file, line and target

#### Scenario: A page README does not name
- **WHEN** the web app registers a page at `coverage` and README.md has no `` `/coverage` ``
- **THEN** `make docs-check` fails, naming the web app's route file and line and the path `/coverage`

#### Scenario: Redirects and parameters are not pages
- **WHEN** the web app registers `chat/:conversationId?`, a route whose element is a redirect, and a catch-all `*`,
  and README.md names `` `/chat` `` only
- **THEN** `make docs-check` does not fail on any of them

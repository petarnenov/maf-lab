# Spec Delta

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
- an active change's proposal has no `Documentation impact` section;
- an active change's proposal has no `## Stopping` section, or the section is empty, or it says neither
  `None — <reason>` nor all four of `Key:`, `Stop:`, `Recorded in:` and `Shown:`, each with something after it
  (stop-anything);
- an active change's proposal has no `## Progress` section, or the section is empty, or it says none of
  `None — <reason>`, `Not yet — <reason>; follow-up: <change>`, or at least one of `Terminal:` and `Page:` with
  something after it — and no `Terminal:` or `Page:` it gives is empty (progress-feedback).
- an active change's proposal has no `## Principles` section, or the section is empty, or it says neither
  `None — <reason>` nor both `SOLID:` and `Standards:` with something after each, or an `Own:` entry in it names no
  `DECISIONS §<n>` (solid-and-standards).
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

#### Scenario: A proposal that does not say how it stops
- **WHEN** an active change's `proposal.md` has no `## Stopping` section
- **THEN** `make docs-check` fails, naming the change and saying the section is missing

#### Scenario: A proposal that leaves out a fact
- **WHEN** a proposal's `## Stopping` section names `Key:`, `Stop:` and `Shown:` but not `Recorded in:`
- **THEN** `make docs-check` fails, naming the change and the missing `Recorded in:`

#### Scenario: A proposal with an empty fact
- **WHEN** a proposal's `## Stopping` section has `Shown:` with nothing after it
- **THEN** `make docs-check` fails, naming the change and the empty `Shown:`

#### Scenario: A change with nothing to stop
- **WHEN** a proposal's `## Stopping` section says `None — it only renames a document`
- **THEN** `make docs-check` accepts it

#### Scenario: None without a reason
- **WHEN** a proposal's `## Stopping` section says only `None`
- **THEN** `make docs-check` fails, saying a reason is needed

#### Scenario: Archived changes are history
- **WHEN** an archived change's proposal has no `## Stopping` section
- **THEN** `make docs-check` does not fail on it

#### Scenario: A proposal that does not say how it shows progress
- **WHEN** an active change's `proposal.md` has no `## Progress` section
- **THEN** `make docs-check` fails, naming the change and saying the section is missing

#### Scenario: A terminal bar is enough
- **WHEN** a proposal's `## Progress` section says only `Terminal: one bar over the documents, done/total`
- **THEN** `make docs-check` accepts it

#### Scenario: An empty page line
- **WHEN** a proposal's `## Progress` section has `Terminal:` with a value and `Page:` with nothing after it
- **THEN** `make docs-check` fails, naming the change and the empty `Page:`

#### Scenario: Not yet, with its follow-up
- **WHEN** a proposal's `## Progress` section says `Not yet — the runner reports no steps; follow-up: add-runner-steps`
- **THEN** `make docs-check` accepts it

#### Scenario: Not yet, without a follow-up
- **WHEN** a proposal's `## Progress` section says `Not yet — later`
- **THEN** `make docs-check` fails, saying it must name the follow-up

#### Scenario: A proposal that does not say what it stands on
- **WHEN** an active change's `proposal.md` has no `## Principles` section
- **THEN** `make docs-check` fails, naming the change and saying the section is missing

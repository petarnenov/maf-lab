# Spec Delta

## Purpose

Keeps maf-lab's documents outside the main specs (README, `docs/`, `openspec/project.md`, `openspec/config.yaml`, the
agent instructions) in step with the code. Facts the code already holds are generated, and everything else is checked.

## ADDED Requirements

### Requirement: Generated blocks have one source each
A fact the repository already holds in code or configuration SHALL appear in a document only inside a generated block,
delimited by `generated:<name>` start and end markers, and SHALL be written there by `make docs`. The generated blocks
SHALL be:
- `make-targets` in `README.md`, from the Makefile's `##` target descriptions;
- `repo-layout` in `openspec/project.md`, from each .NET project's description under `src/` and `tools/` and from a
  checked-in description of each other top-level directory;
- `lb-routes` in `README.md` and `.github/copilot-instructions.md`, from the load-balancer configuration;
- `project-context` in `openspec/config.yaml`, from the whole of `openspec/project.md`.
Every block with the same name SHALL have identical content wherever it appears. Text outside the markers SHALL NOT be
changed by `make docs`.

#### Scenario: A new target reaches the README
- **WHEN** a Makefile target with a `##` description is added and `make docs` is run
- **THEN** the `make-targets` block in README lists that target with its description, and no text outside the block changes

#### Scenario: A new project reaches the layout
- **WHEN** a project with a description is added under `src/` and `make docs` is run
- **THEN** the `repo-layout` block in `openspec/project.md` lists it with that description

#### Scenario: The OpenSpec context follows project.md
- **WHEN** `openspec/project.md` is edited and `make docs` is run
- **THEN** the `context:` of `openspec/config.yaml` equals the new `project.md`, and its `rules` and `operations` are unchanged

#### Scenario: Generation is idempotent
- **WHEN** `make docs` is run twice in a row
- **THEN** the second run changes no file

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

### Requirement: Exemptions are explicit
A route that is deliberately left out of `docs/http-api.md` SHALL be listed in a checked-in exemption list together with
its reason. An exemption without a reason, or one for a route the api does not register, SHALL fail the check.

#### Scenario: Health endpoint exempt
- **WHEN** `/health` is listed as exempt with a reason
- **THEN** `make docs-check` does not require a row for it

#### Scenario: Stale exemption
- **WHEN** the exemption list names a route the api no longer registers
- **THEN** `make docs-check` fails and names the exemption

### Requirement: Every change states its documentation impact
The OpenSpec configuration SHALL require:
- that every proposal contains a `## Documentation impact` section naming each document the change affects, or stating
  why none is affected;
- that every `tasks.md` ends with a task group that updates those documents, runs `make docs`, and runs
  `make docs-check`.
The archive guidance SHALL require `make docs-check` to pass before a change is archived. It SHALL also call for an
advisory, read-only review of the checked documents against the change, which lists prose that may now be stale.
The review SHALL NOT block the archive. Anything it finds is either fixed in the change or reported to the user.

#### Scenario: Proposal without the section
- **WHEN** an active change's `proposal.md` has no `## Documentation impact` section
- **THEN** `make docs-check` fails and names the change

#### Scenario: Change with no documentation impact
- **WHEN** a proposal's `Documentation impact` section says no document is affected and why
- **THEN** `make docs-check` accepts it

#### Scenario: Archive with failing check
- **WHEN** a change is archived while `make docs-check` fails
- **THEN** the archive guidance directs the archiving agent to stop and report the failures instead of archiving

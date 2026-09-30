# Spec Delta

## Purpose
A project-wide rule that no work runs silently: every CLI tool shows a progress bar for the work it does, and every
process started from the UI that can take longer than 3 seconds shows progress in the page's own theme and design.

## ADDED Requirements

### Requirement: CLI tools show a progress bar
Every CLI tool in the repository — the console apps under `src/` and `tools/`, the scripts under `scripts/`, and the
`make` targets that run them — SHALL show a progress bar for the work it does. When the total is known (files,
chunks, cases, attempts, services) the bar MUST be determinate and show done/total and a percentage; when the total is
not known it MUST be indeterminate and show the current step and the elapsed time. The bar MUST update at least once a
second while work continues and MUST NOT carry message content or secrets.

#### Scenario: Known total
- **WHEN** a developer runs a tool that processes a known number of items, for example `make index` or `make eval SUITE=selection`
- **THEN** the terminal shows one bar that advances with each finished item and reads `done/total` and a percentage

#### Scenario: Unknown total
- **WHEN** a tool waits on work whose size it cannot know, for example waiting for services to become healthy
- **THEN** the terminal shows an indeterminate bar with the current step and the elapsed time, updated at least once a second

#### Scenario: Output is not a terminal
- **WHEN** the tool's output is redirected to a file or runs in CI
- **THEN** it prints no redrawn bar and no control characters, only plain progress lines at a bounded rate (a line per step or at most one every 5 seconds)

#### Scenario: The work ends
- **WHEN** the work succeeds, fails or is cancelled
- **THEN** the bar is replaced by one final line that says which, with the count reached and the elapsed time, and the exit code matches

### Requirement: UI-started processes longer than 3 seconds show themed progress
Every process started from the web UI that can take longer than 3 seconds SHALL show progress on the page from the
moment it starts until it ends. The indicator MUST use the page's theme tokens and existing components so it reads
correctly in both light and dark themes, MUST be determinate when the server reports a step count and indeterminate
otherwise, and MUST expose `role="progressbar"` with an accessible label (and `aria-valuenow`/`aria-valuemax` when
determinate). It MUST respect reduced-motion preferences.

#### Scenario: A long action starts
- **WHEN** a user starts an action that can run longer than 3 seconds, for example a test-generation run or an index rebuild
- **THEN** a progress indicator appears at once, in the page's design, and the control that started it cannot start it twice

#### Scenario: Server reports steps
- **WHEN** the server reports how many steps there are and which one is running
- **THEN** the indicator is determinate and names the current step

#### Scenario: Both themes
- **WHEN** the user switches between the light and the dark theme while the process runs
- **THEN** the indicator follows the theme with no hard-coded colours

#### Scenario: Reduced motion
- **WHEN** the user's system asks for reduced motion
- **THEN** the indicator shows progress without animation

#### Scenario: The process ends
- **WHEN** the process succeeds, fails or is cancelled
- **THEN** the indicator is replaced by the outcome in the page's design, and a failure says what failed

### Requirement: Changes are checked against the rule
Every proposal that adds or alters a CLI tool, a `make` target, or a UI action that can start a process longer than
3 seconds SHALL state how it shows progress under this capability. A change that cannot meet the rule MUST say why in
its design and name the follow-up that will.

#### Scenario: A new make target
- **WHEN** a proposal adds a `make` target that runs for more than a moment
- **THEN** its proposal names the progress bar the target shows, and review rejects it otherwise

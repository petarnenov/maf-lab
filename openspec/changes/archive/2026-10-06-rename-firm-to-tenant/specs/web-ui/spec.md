# Spec Delta

## MODIFIED Requirements

### Requirement: Index administration screen
The `/admin/index` screen SHALL let an admin trigger indexing, view drift
percentage, view the distribution of model_version across chunks, and start
the embedding migration. It SHALL be available only to TENANT_ADMIN.

The drift card SHALL also show the graph's drift in the page's theme: how many of the source documents are out of
sync in the graph, or that the graph is unavailable.

#### Scenario: Non-admin
- **WHEN** a USER opens `/admin/index`
- **THEN** access is denied

#### Scenario: Graph in sync
- **WHEN** an admin opens `/admin/index` and the graph matches the corpus
- **THEN** the drift card reads "Graph: 0 of N out of sync" under the index drift

#### Scenario: Graph unavailable
- **WHEN** an admin opens `/admin/index` while the graph store is down
- **THEN** the drift card shows the index drift as before and reads "Graph: unavailable", with no error state for the
  card

### Requirement: Trace from the review queue
The `/admin/feedback` review form SHALL offer the turn's trace to the reviewing TENANT_ADMIN.

#### Scenario: Reviewer opens trace
- **WHEN** a TENANT_ADMIN opens a flagged turn in the review queue
- **THEN** a link or panel shows that turn's trace

### Requirement: Compliance screen
The `/admin/compliance` screen SHALL be available to a TENANT_ADMIN and SHALL show three things for their own tenant:
the state of the audit chain, the record of actions, and a way to produce an export.

The chain state SHALL be stated in words, not only in colour: whether it is intact, how many records were checked,
how many predate the chain, and — when it is broken — which record broke it and when. A broken chain MUST be
unmistakable, and the screen SHALL say that records before the break are unaffected.

The record SHALL be shown newest first with the time, the person, the kind, the action and its outcome, filterable
by person, kind and period, with a way to load older records. The screen MUST NOT imply that identifiers are the
whole story: where an action carries no arguments, it SHALL show that plainly rather than an empty cell.

The export SHALL take a period and, optionally, a person, download the package as a file, and then show the
manifest — the counts, the digest and the chain head — so it can be quoted without opening the file.

The screen SHALL NOT claim more than the system provides: it SHALL state that the chain detects tampering rather
than preventing it.

#### Scenario: Intact chain
- **WHEN** a TENANT_ADMIN opens the screen and the chain is intact
- **THEN** it says so in words, with how many records were checked and how many predate the chain

#### Scenario: Broken chain
- **WHEN** the chain is broken
- **THEN** the screen shows it unmistakably, names the record that broke it and the time, and says that earlier records are unaffected

#### Scenario: Browsing and filtering
- **WHEN** the admin filters by a person and loads more
- **THEN** only that person's actions are listed, newest first, and older ones are appended

#### Scenario: Export
- **WHEN** the admin exports a period
- **THEN** the package downloads as a file and the manifest's counts, digest and chain head are shown on screen

#### Scenario: Nothing recorded yet
- **WHEN** the record is empty for the chosen filters
- **THEN** the screen says so rather than showing an empty table

#### Scenario: Not an admin
- **WHEN** a user who is not a TENANT_ADMIN opens the screen
- **THEN** it shows the same access-denied treatment as the other admin screens

### Requirement: A2A screen
A TENANT_ADMIN SHALL have a screen showing what arrived from partner systems, what this system asked of another
agent, and every push delivery — each with its state and timing — and SHALL be able to cancel a task of their
tenant that is still running. A person who is not a TENANT_ADMIN SHALL NOT reach it.

The screen SHALL also have a read-only section for the test-generation agent, loaded from the api on its own and
shown whether or not any partner has talked to the system: whether the agent is reachable (or not configured), its
card (name, version, description, skill, endpoint, required scope, the partner id the api signs in as), the run
defaults and limits (default model, attempts, tool rounds, test runs, suspected bugs, deadline, no default budget),
the counts of runs by group with a link to the Coverage page, and the most recent runs, each file opening on the
Coverage page. It SHALL use the screen's existing design and theme tokens in both themes.

Each recent run SHALL show how long it took, as the api reports it, in a compact form — seconds under a minute
(`42s`), minutes and two-digit seconds under an hour (`3m 05s`), hours and two-digit minutes beyond (`1h 02m`) — and
`—` when the api reports none. A running run's duration SHALL be marked as running so far and SHALL keep counting on
the page, at least once a second, from the api's value plus the time since that answer arrived, until the next
refresh replaces it.

Each recent run SHALL also show, right after its duration, what it cost as the api reports it, in US dollars: three
decimals under a dollar (`$0.042`), two from a dollar (`$1.27`), `<$0.001` for an amount above zero that would read
as `$0.000`, `$0.00` when the api reports zero, and `—` when the api reports no cost. An amount above zero that the api
marks as an estimate SHALL be prefixed with `≈` (`≈$0.042`). A running run's cost SHALL be marked as so far; it is
brought up to date by the next refresh. The cost SHALL say, on hover, the tokens used, whether the price is an
estimate, and the run's cost cap when it has one.

While the screen's data loads or is refreshed it SHALL show the app's themed progress indicator
(`role="progressbar"` with an accessible label), and the Refresh button SHALL NOT start a second refresh while one is
in flight. A section that fails to load SHALL say what could not be loaded without hiding the other.

#### Scenario: What is there
- **WHEN** a TENANT_ADMIN opens the A2A screen
- **THEN** inbound tasks, outbound consultations and push deliveries are listed, each with its state

#### Scenario: Cancelling from the screen
- **WHEN** the admin cancels a running task
- **THEN** the task is reported cancelled and the list shows it

#### Scenario: Not an admin
- **WHEN** an advisor tries to open it
- **THEN** the screen is not available to them

#### Scenario: Nothing yet
- **WHEN** no agent has talked to this system
- **THEN** the screen says so rather than showing empty tables, and the test-generation agent section is still shown

#### Scenario: The test agent section
- **WHEN** a TENANT_ADMIN opens the screen and the test agent is reachable
- **THEN** the section shows it reachable, its card, its run defaults, the run counts with a link to Coverage, and
  the recent runs with file, state, attempt n/N, coverage, reason, duration, cost and when

#### Scenario: Durations in the recent runs
- **WHEN** the api reports a finished run that took 185 000 ms, a running run at 42 000 ms, and a run with no
  duration
- **THEN** the first reads `3m 05s`, the second reads `42s` marked as so far and a second later `43s`, and the third
  reads `—`

#### Scenario: Costs in the recent runs
- **WHEN** the api reports a finished run that cost 0.291 at an estimated price with a $0.50 cost cap, a running run
  at 1.2745 at a list price, a run at 0.0004, a run at 0, and a run with no cost
- **THEN** the first reads `≈$0.291` with "of $0.50 budget" on hover, the second reads `$1.27` marked as so far, the
  third reads `<$0.001`, the fourth `$0.00`, and the fifth `—`

#### Scenario: The test agent is down
- **WHEN** the api reports the test agent unreachable
- **THEN** the section says so with the reason, and still shows the run defaults and runs

#### Scenario: Loading
- **WHEN** the screen is opened and the api has not answered yet
- **THEN** a themed progress indicator with an accessible label is shown in place of each section until it answers

#### Scenario: The overview fails
- **WHEN** the test agent overview cannot be loaded
- **THEN** the section says so, and the partner activity is still shown

### Requirement: Jev statistics screen
The `/admin/jev` screen SHALL show, for a period the user chooses, the statistics of every Jev call site in chat turns
as charts. It SHALL say that Jev calls made on the A2A partner path are not counted there.

The screen SHALL be reachable from the main navigation, and `/admin/intents` SHALL continue to reach it. Like the other
admin screens, it SHALL be shown only to TENANT_ADMIN.

It SHALL show a cross-cutting overview and a section per call site:

- **Overview:**
  - the total Jev requests over the period;
  - how many requests were unavailable;
  - the requests per classified turn;
  - total requests and unavailability over time, so a degraded Jev is visible across every site at once.
- **Intent:**
  - headline numbers: classified turns, share used, gated and failed, forced-retrieval rate, p90 latency;
  - a diagram of the classification pipeline, with the configured floors and how many turns took each path;
  - turns over time by outcome;
  - timeouts over time;
  - Jev's choice against the intent the turn proceeded with;
  - the reasons an answer was not used;
  - the confidence histogram with the confidence floor marked;
  - the in-domain histogram with the in-domain floor marked;
  - a confidence × in-domain scatter by outcome, with both floors marked;
  - the mean probability per intent;
  - the latency histogram with the timeout marked, and the latency percentiles;
  - the model versions that answered;
  - the history of the `intent` eval: accuracy per language and per split, forcing rates, and the latest run's
    failures.
- **Guardrail:**
  - how many prompts, tool results and reviewer texts were screened, and their decisions;
  - the question that tripped a block or a withholding;
  - the blocked, withheld and unscreened counts;
  - screenings over time, with the unscreened ones marked.
- **Relevance & rerank:**
  - how many searches Jev judged;
  - how many the gate silenced;
  - how many were reranked by Jev;
  - how many were left ungated because Jev was unavailable;
  - the distribution of each search's top relevance against the floor;
  - the judge latency.
- **Routing:**
  - how many data turns there were;
  - how many were routed, and to which tool;
  - why a data turn was not routed;
  - the model calls a turn made, routed against unrouted.

Each chart SHALL:
- name its period;
- identify series by a legend or labels, not by colour alone;
- offer the values behind the marks on hover;
- read as no data, not as zero, when nothing was recorded.

When the statistics cannot be loaded, the screen SHALL say so and stay usable. No message content SHALL appear on the
screen.

#### Scenario: Charts after classified turns
- **WHEN** a TENANT_ADMIN opens `/admin/jev` after turns were classified
- **THEN** the headline numbers, the pipeline diagram and every chart are shown for the chosen period, with the floors
  drawn at 0.5, 0.2 and the timeout

#### Scenario: Nothing classified in the period
- **WHEN** no turn was classified in the chosen period
- **THEN** the screen says there is no data for it instead of drawing empty charts

#### Scenario: Statistics unavailable
- **WHEN** the statistics request fails
- **THEN** the screen shows an error and still renders its controls

#### Scenario: Intent eval history
- **WHEN** intent eval reports exist
- **THEN** the screen draws accuracy per language and per split over the runs, and lists the latest run's failures

#### Scenario: Not an admin
- **WHEN** a USER opens `/admin/jev`
- **THEN** the screen shows access denied, as the other admin screens do

#### Scenario: Every Jev call site has a section
- **WHEN** a TENANT_ADMIN opens `/admin/jev` after turns that screened prompts and tool results, gated and reranked
  searches, and routed data turns
- **THEN** the overview and the guardrail, relevance-and-rerank and routing sections are shown alongside the intent
  section, each with its own charts for the chosen period

#### Scenario: The scope is stated
- **WHEN** a TENANT_ADMIN opens `/admin/jev`
- **THEN** the screen says that it counts Jev calls made by chat turns, and that the A2A partner path is not counted

### Requirement: The Jev statistics screen shows skipped calls
The `/admin/jev` overview SHALL show how many calls an open circuit skipped over the period. It SHALL show the count
per site, next to that site's unavailable requests.

The requests and unavailability timeline SHALL draw the skipped calls as a series of their own. The series SHALL be named
in the legend, and its values SHALL be offered on hover. An outage therefore stays visible after the breaker opens.

When no call was skipped in the period, the skipped count SHALL read 0 and the timeline SHALL draw no skipped series.

#### Scenario: Outage with an open circuit
- **WHEN** a TENANT_ADMIN opens `/admin/jev` for a period in which Jev timed out and the circuit then skipped calls
- **THEN** the overview shows the unavailable requests and the skipped calls as separate numbers per site, and the
  timeline shows both series in the buckets where they occurred

#### Scenario: No skipped calls
- **WHEN** no call was skipped in the chosen period
- **THEN** the skipped count reads 0, and the timeline shows only requests and unavailability

### Requirement: Earlier prompts recalled with the arrow keys
The chat input SHALL let the user recall the prompts they sent earlier in the conversation on screen.

**The history:**
- The history is the user's prompts shown in that conversation, oldest to newest. This includes prompts restored from a
  stored conversation.
- The history SHALL be read from the page's own state. Recalling SHALL send nothing and store nothing.

**ArrowUp:**
- Pressing ArrowUp SHALL replace the input's text with the previous prompt in the history. Each further ArrowUp SHALL
  step one prompt further back.
- At the oldest prompt, ArrowUp SHALL leave the input unchanged.

**ArrowDown:**
- ArrowDown SHALL step one prompt forward.
- Stepping forward past the newest prompt SHALL restore the text the input held before the recall began, which may be
  empty.

**When recall takes over the key:**
- ArrowUp SHALL recall only when the caret is on the input's first line and no text is selected.
- ArrowDown SHALL step forward only during a recall, with the caret on the input's last line and no text selected.
- A key pressed with Shift, Ctrl, Alt or Meta held, or while an input method is composing, SHALL NOT recall.
- In every other case the arrow keys SHALL move the caret as they normally do.

**After a recall:**
- The caret SHALL be placed at the end of the recalled text.

**When a recall ends:**
- Editing the input's text SHALL end the recall. The edited text becomes the draft, and the next ArrowUp starts again
  from the newest prompt.
- Sending a message, starting a new conversation or opening another conversation SHALL end any recall.

#### Scenario: Stepping back through earlier prompts
- **WHEN** the user has sent "first", "second" and "third" in the conversation, and presses ArrowUp in the empty input
  three times
- **THEN** the input shows "third", then "second", then "first"

#### Scenario: The oldest prompt is the limit
- **WHEN** the input shows the oldest prompt and the user presses ArrowUp
- **THEN** the input still shows the oldest prompt

#### Scenario: Stepping forward restores the draft
- **WHEN** the user has typed "half a question", pressed ArrowUp twice, then presses ArrowDown twice
- **THEN** the input shows the newer prompt and then "half a question" again

#### Scenario: No prompts yet
- **WHEN** the conversation has no user prompt and the user presses ArrowUp
- **THEN** the input is unchanged

#### Scenario: Caret movement inside a multi-line draft
- **WHEN** the input holds two lines and the caret is on the second line, and the user presses ArrowUp
- **THEN** the caret moves to the first line and no prompt is recalled

#### Scenario: A stored conversation's prompts
- **WHEN** the user opens a stored conversation whose last question was "status of run 4417" and presses ArrowUp
- **THEN** the input shows "status of run 4417"

#### Scenario: Editing ends the recall
- **WHEN** the user recalls "second", changes it to "second, for tenant B", and presses ArrowUp
- **THEN** the input shows the newest prompt, and ArrowDown past it restores "second, for tenant B"

#### Scenario: Sending ends the recall
- **WHEN** the user recalls a prompt, sends it, and presses ArrowUp in the empty input
- **THEN** the input shows the prompt just sent

#### Scenario: A modifier key does not recall
- **WHEN** the user presses Shift+ArrowUp in the empty input
- **THEN** no prompt is recalled

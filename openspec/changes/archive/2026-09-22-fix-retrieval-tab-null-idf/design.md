# Design

## Context

See proposal.md — Why. Three facts shape the approach:

- The diagnostics payload is already right. `DocumentSearchService` emits `idf: null` together with
  `inVocabulary: false` for a term the BM25 model does not know, and `Math.Round(model.Idf(id), 4)`
  with `inVocabulary: true` for one it does. Nothing on the server needs to move.
- The web app has no error boundary at all — no `componentDidCatch` and no
  `getDerivedStateFromError` anywhere under `web/src`. Any throw during render therefore reaches
  the root and unmounts it. This is why a missing number blanks a conversation.
- Traces are persisted, so this is not only a live-turn problem. Of the 79 stored traces that
  carry query terms, 15 contain at least one `"idf":null` — roughly a fifth of the recorded
  history cannot be opened in the Retrieval tab today, and that history does not heal itself.

The monitor already has a precedent for a number that may be absent: `formatMs` in
`web/src/monitor/traceData.ts` takes `number | null | undefined` and returns `'n/a'`. The
retrieval view uses it for every timing. The IDF column is the one numeric field in that view
that was written as though it could not be missing.

## Goals / Non-Goals

**Goals:**

- The Retrieval tab is openable for every trace the system has ever recorded, live or stored.
- An out-of-vocabulary term reads as a diagnosis, not as an absence. The operator learns that the
  term cannot contribute to sparse matching.
- The type system, not a user's browser, is where the next unguarded arithmetic on a nullable
  trace field is caught.
- One broken view cannot cost the user their conversation.

**Non-Goals:**

- No change to what search computes, scores or emits.
- No global error boundary around the whole application, and no error reporting or telemetry for
  caught render errors. The boundary introduced here wraps the monitor's view area only.
- No audit of every other nullable field across every trace view. The one field that is nullable
  in the retrieval payload is `idf`; `score` in `SearchDiagnostics.Candidates` is always a rounded
  double, and the timings already go through `formatMs`.

## Decisions

### Render off `idf`, explain with `inVocabulary`

The condition for "no weight to show" is `idf === null`, because that is the field being read and
therefore the field TypeScript can force a caller to handle. `inVocabulary` supplies the wording
rather than the branch. The two always agree — the server derives both from the same
`TryGetTermId` call — so branching on the field that would otherwise throw keeps the guard
attached to the hazard instead of to a parallel flag that could drift.

The type in `traceData.ts` becomes `{ term: string; idf: number | null; inVocabulary: boolean }`.
All 79 stored traces carrying terms already include `inVocabulary`, so it is not made optional;
a trace old enough to lack it would render the flag's absence as falsy, which is the safe reading.

Alternative considered: a discriminated union (`{ inVocabulary: true; idf: number } | { inVocabulary: false }`).
It would make the invalid state unrepresentable, but it forces every reader through a narrowing
step for one table cell in one view, and it models a shape the JSON does not actually have —
the server emits the `idf` key either way, as `null`. Rejected as heavier than the problem.

Alternative considered: coerce the missing weight to `0`. Rejected — the specs now forbid it, and
it is actively misleading: `0` is a legitimate IDF for a term present in every document, which is
the opposite diagnosis from a term present in none.

### A hand-written error boundary, not a dependency

React 19 still has no hook form of an error boundary; `getDerivedStateFromError` on a class
component remains the only supported mechanism. `react-error-boundary` would add a dependency,
and with it a DECISIONS.md entry and a version to keep from drifting, to avoid writing roughly
thirty lines. The project's conventions favour the smaller footprint. A small class component
under `web/src/components/` it is.

Alternative considered: React 19's root-level `onUncaughtError`. It observes errors but does not
contain them — the root still unmounts, which is the failure being fixed. It is a reporting hook,
not a boundary.

### The boundary wraps the tab body, and resets by key

It goes around the view area inside `MonitorPanel` — the `role="tabpanel"` element — not around
the whole panel and not around the whole page. Placed there, the header, the statistics chips, the
time-travel bar and the tab strip all survive a failing view, so the user can navigate away from
the broken view rather than being stuck in front of it.

Recovery comes from keying the boundary by the selected tab id. React discards and remounts a
subtree whose `key` changes, which resets the boundary's error state as a consequence of the
navigation the user was going to make anyway. This satisfies the "recovering without a reload"
scenario with no reset button and no `useEffect`.

Alternative considered: a "try again" button inside the fallback. It is a second control for
something tab selection already does, and a view that fails deterministically — which this class
of bug does — would fail again immediately, teaching the user the button is broken.

### What the fallback may say

The fallback names the view and says it could not be shown. It does not render the error's
message, and the boundary does not put the error on the page in any form. This is the same rule
the web-ui spec already applies to chat failures under "An error shows the face it deserves" —
nothing internal reaches the page — and the new requirement is asserted the same way, against
rendered output. `componentDidCatch` may log to the console for development; the console is not
the page.

## Risks / Trade-offs

- **A boundary can hide a defect that used to be loud.** A crash that blanked the screen was
  impossible to ignore; a contained one is easy to scroll past. → The fallback is visible and
  names the failing view rather than rendering empty, the boundary logs to the console, and it is
  deliberately scoped to the monitor's view area so a failure anywhere else in the app is as loud
  as it is today.
- **Keying by tab id resets more than the error.** Remounting on every tab switch discards any
  state a view holds internally. → The tab views are derived entirely from `events` and props and
  hold no state across switches today; the panel's own state, including time travel, lives above
  the boundary and is untouched. A future view that needs to keep state across switches would
  need to lift it, which is the right place for it regardless.
- **The OOV wording could be read as a system failure.** "No IDF" might look like something broke
  rather than a fact about the corpus. → The text says what it means for retrieval — the term
  cannot match on BM25 — rather than reporting a missing value.
- **Only the retrieval view is audited here.** Another view may hold the same latent assumption. →
  The boundary now contains it to that view instead of the whole screen, which is the general
  answer; the specific audit of the retrieval payload is done and recorded above.

## Migration Plan

None required. The change is client-side rendering only: no schema, no stored data, no API
contract. Stored traces that cannot be opened today become openable as soon as the new bundle is
served; nothing needs backfilling.

## Context

`make docs-check` (`scripts/docs.py`) checks what can be read from code without a model: generated blocks, api routes
against `docs/http-api.md`, `make` references, model names, relative links and proposals. Everything else in README is
prose, checked by review at archive time. The audit behind this change found nine prose mismatches; one class — a page
the web app has and README does not name — is as mechanical as the route rule and was missed for a whole feature
(`/coverage`).

## Goals / Non-Goals

**Goals:** README states what the code does today; the one mechanical class of drift found is checked from now on.

**Non-Goals:** checking prose in general (ports, replica counts, tolerances, section coverage); changing any
application behaviour; documenting pages anywhere but README.

## Decisions

- **Docs follow code.** Every finding is fixed in the document. None of them showed the code deviating from a spec in
  `openspec/specs`, so no code changes beyond the Makefile's `##` description (itself a docs source).
- **Pages are read with a regular expression, not a TypeScript parser.** The check is standard-library Python by
  design (it runs in CI's `specs` job with nothing installed). `App.tsx` declares routes as `<Route path="…"
  element={<X …` in one consistent style; the pattern takes the path and the element's first tag, treats `Navigate`
  as a redirect, and skips `*` and `index` routes. A route that does not match the pattern is not seen — the failure
  mode is a missed check, never a false alarm.
- **Name, not location.** README must contain `` `/path` `` somewhere. Requiring it in the Screens list would couple the
  check to one sentence's wording; any code-formatted mention (a link whose text is the path counts) shows the reader the page
  exists.
- **Parameter segments are dropped.** `chat/:conversationId?` is the page `/chat`; README documents `/chat/{id}` in
  prose, and the bare path is the name a reader navigates to.
- **No exemption list.** Every page today is meant for readers. If one ever is not, a `[pages.unlisted]` table with
  reasons, like `[routes.undocumented]`, is the place; adding it now would be configuration nothing uses.
- **The 0.21 → 0.68 figure stays, as history.** It is the measurement that justified query translation; the current
  figures live in `evals/baseline.json`, and README points there rather than copying numbers that the next accepted
  run would make stale again.

## Risks / Trade-offs

- A page added in another style (lazy routes, a route table) escapes the rule → the regex is covered by tests with the
  shapes `App.tsx` uses; a new shape is a test plus a pattern change.
- README prose can still drift in every way the check cannot see → unchanged from today: the archive guidance's
  read-only review remains the safeguard.

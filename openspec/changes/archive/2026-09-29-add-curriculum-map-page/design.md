# Design

## Context

The web app's screens share `components/Page.module.css` (`page`, `heading`, `subheading`, `muted`, `mono`, `tag`,
`card`) and the colour tokens in `index.css`, which already switch for dark mode. Routes are declared in `App.tsx` and
the header's links in `components/Layout.tsx`. Page tests render through `test/render.tsx`; the topology test already
reads a repository file with `node:fs`, so a test may look outside `web/`.

See proposal.md for motivation and specs/web-ui/spec.md for the requirement.

## Goals / Non-Goals

**Goals:**
- Content that is easy to review in a diff and hard to let rot.
- A page that reads like the other screens: same width, headings, muted notes, tags and cards.

**Non-Goals:**
- Rendering or shipping the plan PDF, or quoting it at length.
- Live data (health, counts) on this page — the linked screens already show that.
- Translating the page: repository artefacts are in English.

## Decisions

1. **Content as a typed module, not markdown and not an endpoint.** `curriculum.ts` exports
   `CURRICULUM: CurriculumSection[]`, each section with `entries: { concept, summary, paths, spec, screen? }` and a
   separate `NOT_COVERED: { topic, reason }[]`. Typed data lets the test check every entry and keeps the component a
   plain renderer. A markdown file would need a parser and could not be checked field by field; an api endpoint would
   add a token requirement for static text.
2. **The test guards the links to the repository.** For every entry the test asserts that each path exists relative
   to the repository root, that `spec` names a directory under `openspec/specs/`, and that `screen` is one of the
   routes in `App.tsx`. A rename that breaks the map fails `make test` instead of leaving a dead reference.
3. **No session gate.** Unlike the other screens the page never calls `useApi`, so it renders before a persona is
   picked (spec scenario "Readable without a persona").
4. **Layout.** One `<section>` per day with an `h2`, then each concept as a card-like `<article>`: `h3` concept, the
   summary paragraph, a line of `mono` paths, a `tag` for the spec and a router `Link` to the screen. A short table of
   contents at the top jumps to the sections by anchor. A local CSS module adds only the grid and spacing on top of
   `Page.module.css`; the entries stack to one column at narrow widths.
5. **Nav placement.** "Curriculum" goes last in the header links, after the admin screens, since it is reference
   material rather than a working screen.

## Risks / Trade-offs

- [Summaries drift from behaviour while paths still exist] → The test only proves paths exist. Each summary is
  written from the code and the spec it names; a change that alters a concept's behaviour updates its entry in the
  same commit, as with any spec.
- [Overstating coverage] → The not-covered section is required by the spec, and every summary says what the lab does,
  not what the plan says.

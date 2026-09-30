# Design

## Context

The stack runs behind the balancer at `http://localhost:7171`. The web app keeps its session in `sessionStorage`
under `maf-lab.session` (`{ token, expiresAt, user }`), and the token comes from `POST /dev/token` for a persona from
`GET /dev/users`. The theme mode lands on `<html data-theme="light|dark">` (`web/src/theme/theme.ts`). The repository
has no images yet, no Git LFS, and no Playwright anywhere; `web/` tests use Vitest.

## Goals / Non-Goals

**Goals:**
- One command (`make screenshots`) re-takes every screenshot from the running stack, so the README stays current.
- Real captures over the seeded sample corpus, readable at GitHub's content width, matching the reader's theme.

**Non-Goals:**
- Running the capture in CI, or committing images automatically.
- Visual regression testing: images are not compared with earlier ones.
- Changing the UI to look better in captures.

## Decisions

**A separate package in `tools/screenshots/`, not a `web/` dev dependency.** Putting Playwright in `web/` would
lengthen every `npm ci` in CI and mix a docs tool into the app's dependency tree. A sibling package with its own
`package.json` and lockfile, next to `tools/Maf.Lab.A2AProbe`, keeps it opt-in. It uses the `playwright` library
(pinned exact, recorded in DECISIONS.md) and not `@playwright/test`, because this is a script, not a test suite. It is
plain ESM JavaScript (`capture.mjs`), so there is no build step.

**Log in through the API, not the picker.** The script calls `POST /dev/token` for `alice` (firm-a, FIRM_ADMIN, who
can open every admin screen) and seeds `sessionStorage` with an init script before the first navigation. The token
exists only in the browser context's memory and is never written to disk or logged.

**Each screen is a small recipe: navigate, bring into state, wait, capture.** The chat recipes also collapse the
history (it lists the persona's earlier test conversations) and scroll the question to the top of the chat pane. A recipe is a function in one array
(`name`, `url`, `prepare(page)`). The chat and code-snippets recipes type a fixed question and wait until the turn
is finished, identified by the UI's own finished-turn marker and not by a fixed sleep. Other recipes wait for their
data to render. `SHOTS=a,b` filters the array. A recipe that times out fails the run with the screen's name and
keeps the files already written, so one broken screen does not wipe the set.

**Light and dark from the same page state.** The app stays in "system" mode, which follows the OS through
`color-scheme`. After `prepare`, the script emulates a light OS scheme, captures, emulates dark, and captures again.
So both variants show the same live answer, and the theme button truthfully reads "System". Forcing `data-theme`
was tried first, and it left the button reading "System" over a forced theme. README uses
`<picture><source media="(prefers-color-scheme: dark)" srcset="…-dark.png"><img src="…-light.png" alt="…"></picture>`.

**Viewport 1440×900, device scale factor 1600/1440.** The two-pane layout needs a wide window, and the fractional
scale factor makes Chromium write 1600-px-wide images directly, with no resize step and no macOS-only `sips`.
Screenshots are page-only (`page.screenshot`), so no browser chrome or address bar is captured. Files are PNG,
because JPEG smears UI text. The script prints each file's size and warns above 400 KB.

**`make screenshots` guards its prerequisites.** It needs npm and a healthy stack (a `curl` against the
balancer). It runs `npm ci` and `npx playwright install chromium` only when they are missing, then runs the script.
It is not part of `ci` or `verify`.

**README layout.** The chat is the hero image under the introduction. Jev and evals, which no section is about,
sit in a two-cell HTML table (a markdown table would render an empty header row) after the paragraph under the
diagram. Topology, code snippets and compliance each appear once, full width, in the section that describes them.

**The architecture diagram is Mermaid, not an exported image.** GitHub renders Mermaid natively and follows the
reader's theme, and the diagram stays a diff-able text next to the prose it explains. The alternatives were:
- an SVG export of `docs/topology.drawio`, which needs draw.io to regenerate and a light/dark pair;
- the `/topology` screenshot, which is already in the README and shows health rather than wiring.

The diagram keeps edges few, so the layout stays readable. Detail such as tool names, replicas and what Jev judges
goes into node labels. Observability is one node. Nodes use colour classes (entry, services, stores, models), and
groups have transparent fills, so both GitHub themes read well. The rendering was checked with Mermaid 11 in both
themes.

**Syncing facts, not rewriting.** Each stale statement is corrected where it stands, with the source it was checked
against: compose replicas, the nginx routes, `make verify`'s actual output, `evals/domain.jsonl` row count,
`scripts/dev.sh`, `make help`. Sections that were accurate are left alone.

**Content safety.** Only seeded personas and sample data appear. The script never types a key, and the monitor shows
the system prompt by decision, which is fine to publish. Each new set is still checked by eye before it is
committed.

## Risks / Trade-offs

- [Live model answers vary between runs] → the questions are fixed and retrieval-heavy, so the layout is stable
  even when the wording is not. The images are illustrations, not a contract.
- [Selectors break when the UI changes] → the recipes prefer roles and visible text over CSS classes. A broken
  recipe names its screen and leaves the rest of the set intact.
- [The repository grows with every re-take] → 1600 px PNGs of about 200–400 KB each, and re-takes happen when the UI
  changes visibly, not on a schedule.
- [Each run spends Ollama Cloud and Jev credit] → only two recipes ask a question. `SHOTS=` re-takes one screen
  without the chat turns.

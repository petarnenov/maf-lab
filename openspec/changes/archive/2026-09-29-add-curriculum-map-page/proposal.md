# Proposal

## Why

maf-lab was built to exercise, in code, the concepts of the 5-day Fullstack AI Engineer study plan (revision 7): the
agent loop, MCP after the 2026-07-28 revision, RAG as a tool over a multi-tenant Qdrant index, evals, prompt
injection, A2A and AG-UI. Nothing in the app says so. Someone reading the code, or the author before an interview,
has to reconstruct from memory which screen, spec and class shows which part of the plan, and which parts the lab
deliberately does not cover. One page that answers "where is this concept used here" in a few sentences per concept
closes that gap.

## What Changes

- A new `/curriculum` screen in the web app, linked from the main navigation as "Curriculum".
- The screen is organised by the plan's days (agent basics and context, MCP, RAG as a tool, A2A / multi-agent /
  AG-UI, system design), then a section for the plan's rules:
  - Each concept gets a short title and two or three sentences on how the lab applies it.
  - Each concept names the files that implement it, the spec that states it and, where one exists, links to the
    screen where it can be seen.
- A section lists the plan's topics the lab does not implement, and why (for example the Java / LangChain4j side),
  so the page never overstates coverage.
- The content is static and ships with the web bundle: no endpoint, no token, no persona needed to read it.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `web-ui`: a new requirement for the curriculum map screen — its grouping, what each entry shows, the
  "not covered" section, and that it renders without a session.

## Impact

- **web:**
  - `web/src/curriculum/` — `CurriculumPage.tsx`, a typed content module `curriculum.ts`, a CSS module and a test.
  - `App.tsx` (route) and `components/Layout.tsx` (nav link).
- **Not changed:** no api, MCP server, index or eval change; the plan PDF itself is not committed.

# Tasks

## 1. Content

- [x] 1.1 Add `web/src/curriculum/curriculum.ts`: the section and entry types, one section per day of the plan, a
  rules section, and `NOT_COVERED`. Write each summary from the code and spec it cites.

## 2. Screen

- [x] 2.1 Add `web/src/curriculum/CurriculumPage.tsx` and `Curriculum.module.css`: table of contents, sections,
  entry cards with paths, spec tag and screen link, then the not-covered section. No api call, no session gate.
- [x] 2.2 Register the `/curriculum` route in `App.tsx` and the "Curriculum" link in `components/Layout.tsx`.

## 3. Tests

- [x] 3.1 `CurriculumPage.test.tsx`:
  - the sections render in order, ending with rules and not-covered;
  - it renders with no session and makes no fetch;
  - a screen link points at its route.
- [x] 3.2 A content test: every path exists in the repository, every spec is a directory under `openspec/specs/`,
  every screen is a route in `App.tsx`, and every summary is non-empty.

## 4. Verify

- [x] 4.1 `make lint` and `make test` pass.
- [x] 4.2 Open `/curriculum` in the running app (light and dark, narrow width) and check it reads like the other
  screens.

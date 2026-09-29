# Design

## Context

Colours are CSS custom properties on `:root`, redefined in a `@media (prefers-color-scheme: dark)` block
(`index.css`, and the chart slots in `IntentStats.module.css`). Nothing else reads the theme: no JS asks
`matchMedia`. A few module styles hard-code light colours.

## Decisions

**1. One declaration per token, with `light-dark()`.** Each token becomes `--bg: light-dark(#f6f7f9, #111418)`.
`light-dark()` picks by the element's used `color-scheme`, so the mode is set in one place:
- `:root { color-scheme: light dark }` follows the system;
- `:root[data-theme='light'] { color-scheme: light }` and `[data-theme='dark'] { color-scheme: dark }` force it.

*Alternative:* keep the media block and add `[data-theme]` copies. That triples every token list and lets them drift.
`light-dark()` is supported by every current browser (Baseline 2024); this is a dev tool, not a public site.

**2. The mode is an attribute on `<html>`.** System removes `data-theme`; Light and Dark set it. Native controls,
scrollbars and form fields follow `color-scheme` for free.

**3. Applied before the first paint.** An inline script in `index.html` reads the stored mode and sets the attribute
before the stylesheet applies. The app's module script runs too late to avoid a flash of the system theme. No CSP is
set for the web container, so an inline script is allowed; if one is added, the script needs its hash.

**4. Stored in `localStorage`** under `maf-lab.theme` as `light` or `dark`; System removes the key. Every read and
write is wrapped in `try/catch`: storage may be blocked, and then the choice lasts for the page only. The theme is a
per-browser convenience, not a user setting, so nothing goes to the server.

**5. One button that cycles** System → Light → Dark → System. Its visible text names the current mode (`◐ System`,
`☀ Light`, `☾ Dark`); its accessible name is "Theme: <mode>" and its title names the next mode. A three-way select
would take more room in an already full header.

## Risks

- Hard-coded colours elsewhere still ignore the theme. This change fixes the rewind banner, which reads worst; the
  rest are left for a follow-up and listed in the tasks.

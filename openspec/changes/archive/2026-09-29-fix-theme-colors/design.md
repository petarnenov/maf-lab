# Design

## Context

Theme tokens live on `:root` in `index.css` as `light-dark(light, dark)`, and `color-scheme` picks between them
(`add-theme-toggle`). Components that set colour from TypeScript do it through inline `style`. An inline style can
hold `var(--token)` just as well as a hex value, and it resolves with the element's theme.

## Decisions

**1. Category colours are tokens, and the helpers return `var()` references.** `kindColor`, `frameColor` and
`domainColor` keep their mapping and return `var(--kind-model)` and the like. The same kind has the same colour
everywhere, and the theme needs no JavaScript. *Alternative:* read the theme in JS and pick a hex value. That needs a
`matchMedia` listener plus a re-render on every change, for no gain.

**2. The tokens.** Light values are unchanged from today's hex values. Dark values are lighter tints of the same hue.
Measured against the surfaces (`#ffffff` / `#f6f7f9` light, `#181c21` / `#111418` dark):

| token | light | dark | light on surface | dark on surface | badge text (light / dark) |
|---|---|---|---|---|---|
| `--kind-neutral` | #5d6673 | #9aa4b2 | 5.81 | 6.79 | 5.81 / 7.50 |
| `--kind-model` | #2f5bd3 | #6b8ff0 | 5.90 | 5.55 | 5.90 / 6.13 |
| `--kind-danger` | #b42318 | #f07167 | 6.57 | 5.92 | 6.57 / 6.54 |
| `--kind-tool` | #8e44ad | #c08ae0 | 5.87 | 6.48 | 5.87 / 7.16 |
| `--kind-retrieval` | #1d7a3c | #4cc27a | 5.38 | 7.58 | 5.38 / 8.38 |
| `--kind-check` | #3f7d20 | #9ccc65 | 5.04 | 9.16 | 5.04 / 10.12 |
| `--kind-domain` | #0f7b5f | #3cc4a0 | 5.23 | 7.82 | 5.23 / 8.64 |
| `--kind-context` | #8a6d3b | #d0ad6e | 4.85 | 8.06 | 4.85 / 8.90 |
| `--kind-other` | #0e7490 | #4fb8d4 | 5.36 | 7.45 | 5.36 / 8.23 |
| `--warn` | #8a5a00 | #e0b25c | 5.93 | 8.71 | — |

Every value clears 3:1 as a mark and 4.5:1 as text. `--warn` replaces Topology's `#b8860b`, which was 3.25:1 as
text on white.

**3. Text on a filled colour uses `--on-accent`**: `#ffffff` in light and `#0c1020` in dark. Every fill above is dark
in the light theme and light in the dark theme, so one ink token serves all of them.

**4. Scrims stay fixed.** `rgb(0 0 0 / 25–35%)` behind the drawer and dialogs dims whatever is underneath in either
theme.

## Risks

- A test that compares an inline `background` to `kindColor(...)` keeps working, since both sides are the same
  string. A test that expects a hex value would need changing; none does.

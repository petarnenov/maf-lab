# Design

## Context

`Layout` renders `<header>` and `<main>` inside `.shell` (a `min-height: 100vh` flex column). The header wraps
(`flex-wrap: wrap`); measured on the running app it is about 56 pixels tall in a 2560-pixel window, 189 at 1440
(brand, two rows of links, the persona row), 281 at 800 and 433 at 375. The chat page sizes itself to the window with
`calc(100vh - 120px)`, a guess at the header plus `main`'s padding. Overlays use `position: fixed` with z-index 10
(coverage dialog), 14/15 (chat history drawer backdrop/drawer) and 20 (history dialog). In-flow positioned content
uses z-index 1, 2 and 5 (coverage line tip, intent tip, history row menu); chat source tables pin their first column
with `position: sticky; left: 0` and no z-index. Curriculum sections have `scroll-margin-top: 16px`.

## Decisions

### 1. `position: sticky`, not `fixed`

Sticky keeps the header in the document flow, so `main` needs no padding equal to a height it does not know, and a
non-pinned header (decision 3) is the same element with one property changed. `.header` gets `position: sticky;
top: 0` only when pinned.

### 2. The height is measured, and published as one variable

A pure-CSS approach cannot know the wrapped height. A `useStickyHeader` hook observes the header with a
`ResizeObserver` (and listens to window `resize`, since the viewport height can change without the header changing)
and sets `--app-header-offset` on `document.documentElement`: the header's height in pixels when pinned, `0px`
otherwise. One variable serves every consumer:

- `html { scroll-padding-top: var(--app-header-offset, 0px) }` makes every anchor jump, `scrollIntoView` and
  keyboard page scroll of the window stop below the header. The Curriculum sections' own `scroll-margin-top: 16px`
  still adds breathing room on top of it.
- The chat page's window-sized grid uses `calc(100vh - var(--app-header-offset, 80px) - 40px)` (40 is `main`'s
  vertical padding), so it fits exactly below the header with no window scroll. The narrow (stacked) chat pane uses the
  same expression.

The hook runs in a layout effect so the first paint already has the value, and removes the variable when the layout
unmounts. Where `ResizeObserver` is missing (old browsers, jsdom) it measures once and on window resize.

### 3. Pinned only when the header takes at most a third of the window

A width breakpoint would be a proxy: the header's height depends on the width, the persona row and the font size,
and what matters is how much of the window's height it takes. The hook already knows both numbers, so it pins the
header only when `height <= innerHeight / 3`. At 1440×900 (189 of 900), 800×900 (281 of 900) and 1920×1080 it is
pinned; on a 375×667 phone (433) or a 1440×500 window (189 > 166) it scrolls away as before. The decision is exposed as
`data-sticky="true|false"` on the header, which the CSS keys on. Pinning does not change the header's height, so the
decision cannot oscillate. In print the header is never pinned (`@media print`).

### 4. Layering: z-index 8

The header needs a z-index because in-flow content positions itself with z-index up to 5 (the history row menu), and
those must pass under the header when scrolled. It must stay under the lowest overlay, 10. It gets 8, with a comment in
`Layout.module.css` naming the neighbours. `main` does not create a stacking context, so the comparison is global.
Sticky table cells (z-index auto) and the coverage line tips (1) also pass under it. The history drawer is
`position: fixed; top: 0` at 15, so it covers the pinned header, as a drawer should.

### 5. No transition

The header does not animate between pinned and not pinned, so `prefers-reduced-motion` needs nothing.

## Risks / Trade-offs

- A 189-pixel header on a 1440×900 laptop takes a fifth of the screen. Accepted: the user asked for the whole section
  to stay; compacting the header is a separate change.
- `scroll-padding-top` also applies to the window's own `scrollIntoView` calls (chat's end-of-thread scroll); that is
  the wanted effect: nothing scrolls under the header.

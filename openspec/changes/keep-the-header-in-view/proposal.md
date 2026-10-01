# Proposal

## Why

The app's header (brand, main navigation, persona picker, theme button) scrolls away with the page. On long pages
(Curriculum, Coverage, Evals, Telemetry, the admin screens) a user who has scrolled down must scroll back to the top
to switch screens, change persona or switch theme. The header should stay in view.

## What Changes

- The header stays pinned to the top of the window while the page scrolls vertically, on every screen and in both
  themes. It keeps its opaque surface background and its bottom border, and it wraps to as many rows as the window
  width needs; nothing assumes a fixed height.
- The page measures the header's real height as it wraps and publishes it to the page. In-page jumps (the Curriculum
  section links, any `#anchor`, and scrolling an element into view) stop below the header instead of under it.
- The chat screen, which sizes itself to the window, subtracts the header's real height instead of a fixed guess, so
  it fits below the header without a second, window-level scroll.
- The header sits above page content (tooltips, menus, sticky table columns) and below the app's overlays (the
  coverage dialog, the chat history drawer and its backdrop, the history rename/delete dialog).
- When the header would take more than a third of the window's height (a phone, or a short window), it is not
  pinned and scrolls away as before, so it never eats most of a small screen. It is never pinned in print.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `web-ui`: a new requirement, "The header stays in view while the page scrolls".

## Impact

- `web/src/components/Layout.tsx` and `Layout.module.css`: the sticky header and its layering.
- A new hook `web/src/components/useStickyHeader.ts` that measures the header and decides whether it is pinned.
- `web/src/index.css`: the root scroll padding that follows the pinned header's height.
- `web/src/chat/ChatPage.module.css`: the window-sized panes subtract the header's height.
- Vitest tests in `web/src/components`.

No route, make target, project, model, package or load-balancer location changes. No Jev call is added or changed.
No CLI tool, make target or UI action that starts a process is added or changed, so there is no new progress to show.

## Documentation impact

None. README.md, CLAUDE.md, docs/*.md, openspec/project.md and .github/copilot-instructions.md do not describe the
header's scrolling behaviour or its layout, so nothing they say becomes untrue or incomplete.

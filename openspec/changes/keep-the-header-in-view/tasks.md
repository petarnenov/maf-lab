# Tasks

## 1. Header

- [ ] 1.1 `useStickyHeader`: measure the header (ResizeObserver plus window resize), decide pinned when its height is
  at most a third of the window, set `--app-header-offset` on the root (height or `0px`), clean up on unmount
- [ ] 1.2 `Layout`: attach the hook to `<header>` and expose `data-sticky`
- [ ] 1.3 `Layout.module.css`: `position: sticky; top: 0; z-index: 8` when `data-sticky="true"`, never in print
- [ ] 1.4 `index.css`: `html { scroll-padding-top: var(--app-header-offset, 0px) }`

## 2. Window-sized screens

- [ ] 2.1 `ChatPage.module.css`: the grid and the narrow chat pane subtract `--app-header-offset` instead of a fixed
  guess

## 3. Tests and checks

- [ ] 3.1 Vitest: the header is pinned and publishes its height; a tall header is not pinned and publishes `0px`; the
  value follows a resize; the variable is removed on unmount; the root scroll padding uses the variable
- [ ] 3.2 Visual check in a browser: long Curriculum page scrolled, a section link, dark theme, 800-pixel window,
  375×667 window, chat at 1440×900
- [ ] 3.3 `make test-web`, `make lint-web`, `make build-web`, `make docs`, `make docs-check`,
  `openspec validate --specs --strict`

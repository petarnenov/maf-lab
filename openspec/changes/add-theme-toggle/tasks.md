# Tasks

## 1. Web

- [x] 1.1 Move the tokens in `index.css` and `IntentStats.module.css` to `light-dark()`, with `color-scheme` set by
  `data-theme` on `<html>`, and put the rewind banner in `ChatPage.module.css` on theme tokens. Verify with
  `make build-web`.
- [x] 1.2 Add the inline script to `index.html` and a `theme/` module: `useTheme` (mode, cycle, storage in
  `try/catch`) and `ThemeButton`, placed in `Layout` next to the persona picker. Verify with tests:
  - the button cycles System → Light → Dark → System, and its accessible name follows;
  - Light and Dark set `data-theme` and store the choice, System removes both;
  - a stored choice is read on mount;
  - a throwing `localStorage` still changes the theme.

## 2. Verification

- [x] 2.1 Run `make test`, `make lint` and `make build-web`; all green.
- [x] 2.2 Rebuild with `make`. At http://localhost:7171: the button switches the chat, the monitor and the Jev page
  between themes; Dark survives a reload; System follows an emulated `prefers-color-scheme` change.
- [x] 2.3 Run `openspec validate add-theme-toggle --strict`; valid.
- [x] 2.4 List the other hard-coded colours that ignore the theme (topology, monitor, confirmation card, telemetry)
  in the summary as a follow-up.

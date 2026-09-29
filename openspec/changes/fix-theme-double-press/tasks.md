# Tasks

## 1. Web

- [x] 1.1 Add `currentTheme()`, which reads the mode from `<html>`, and use it in `ThemeButton` to compute the next
  mode. Verify with a test that two clicks in one `act` move System → Dark. The test fails before the fix, landing on
  Light.

## 2. Verification

- [x] 2.1 Run `make test`, `make lint` and `make build-web`; all green.
- [x] 2.2 Run `openspec validate fix-theme-double-press --strict`; valid.

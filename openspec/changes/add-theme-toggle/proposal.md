# Proposal

## Why

The web app follows the operating system's light or dark setting and offers no way to change it. A person who wants
the other theme for this app alone, for a screenshot or a demo, has to change the whole system. Some colours also
ignore the theme: the time-travel banner is always cream with light text in the dark theme, which is hard to read.

## What Changes

- **A theme button in the header**, next to the persona picker. It cycles through three modes: System (the default),
  Light and Dark, and says which one is on.
- **The choice is remembered** in this browser, and applied before the first paint so a reload does not flash the
  other theme.
- **System follows the operating system live**, including a change made while the app is open.
- **The theme colours are declared once**, with CSS `light-dark()` and `color-scheme`, so the button, the system
  setting and every component read the same tokens. The time-travel banner uses theme tokens instead of fixed colours.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `web-ui`: the user can choose the theme.

## Impact

- **web:**
  - `index.css` and `IntentStats.module.css`: tokens through `light-dark()`, no `prefers-color-scheme` blocks.
  - `index.html`: a small inline script that applies the stored choice before the app loads.
  - A `theme/` module (`useTheme`, `ThemeButton`) and the button in `Layout`.
  - `ChatPage.module.css`: the rewind banner on theme tokens.
- No api change, no packages.

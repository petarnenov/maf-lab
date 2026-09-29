# Proposal

## Why

`add-theme-toggle` put the app's tokens on `light-dark()`, but several colours are still fixed and ignore the theme:

- **The monitor's category colours** (trace kinds on the timeline, AG-UI frame families, domains) are light-theme
  tones set from TypeScript. On the dark surface they fall below 3:1 (2.6–2.95 for red, blue, purple and grey), so
  the timeline bars nearly vanish.
- **Topology's "degraded" amber** (`#b8860b`) is 3.25:1 as text on white, under the 4.5:1 text needs.
- **White text on theme colours**: the Approve button (`--accent`), the history "Delete" button (`--danger`) and the
  monitor's kind and domain badges use `#fff`. In the dark theme those backgrounds are light, so the text reads poorly.
- **`--warn` is used but never defined**, so the "service unavailable" error always takes its light fallback.
- The monitor's current-step outline, its system and tool message borders, and the telemetry bars are fixed hex
  values.

## What Changes

- **Category tokens.** Nine monitor category colours and `--warn` become theme tokens, each validated against both
  surfaces: at least 3:1 as a mark and at least 4.5:1 with its badge text.
- **Text on a filled colour** uses `--on-accent`: white in light, near-black in dark.
- The TypeScript colour helpers return `var(--…)` references instead of hex values.
- Every remaining fixed colour in the stylesheets moves onto a token. Only translucent black scrims behind dialogs and
  drawers stay, since they read the same in both themes.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `web-ui`: every colour on screen follows the chosen theme, at stated contrast.

## Impact

- **web:**
  - `index.css`: the category tokens and `--warn`.
  - `monitor/kindColors.ts`, `monitor/domainData.ts`, `MonitorTabs.tsx` (`frameColor`).
  - CSS modules: `MonitorPanel`, `DomainsTab`, `Topology`, `ConfirmationCard`, `HistorySidebar`, `TelemetryPage`,
    `ChatPage`.
- No api change, no packages.

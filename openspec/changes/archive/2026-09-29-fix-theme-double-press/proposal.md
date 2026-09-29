# Proposal

## Why

The theme button computes the next mode from the mode it rendered with. Two presses that land before React renders
again both read the same mode. From System, a fast double press ends on Light instead of Dark: the second press is
lost.

## What Changes

- The button computes the next mode from the theme applied to the page (`data-theme` on `<html>`). That is always
  current, because the press itself sets it. The rendered mode is only used for the label.
- The requirement states that every press counts, however fast.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `web-ui`: "The user can choose the theme" says presses faster than a redraw each count.

## Impact

- **web:** `theme/theme.ts` (`currentTheme`), `theme/ThemeButton.tsx`, and a test.
- No api change, no packages.

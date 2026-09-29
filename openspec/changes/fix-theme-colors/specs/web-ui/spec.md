# Spec Delta

## ADDED Requirements

### Requirement: Every colour follows the theme
Every colour the app draws SHALL come from the active theme, in the light theme and in the dark theme alike. This
covers text, borders, fills, chart and timeline marks, badges and status colours. The one exception is a translucent
scrim behind a dialog or drawer.

**Contrast.** In each theme:
- text SHALL have a contrast of at least 4.5:1 with what it sits on;
- a mark that carries meaning (a timeline bar, a status outline, a category badge) SHALL have at least 3:1 with the
  surface it sits on;
- text on a filled badge or button SHALL have at least 4.5:1 with the fill.

**Categories.** A category colour (a trace kind, an AG-UI frame family, a domain, a service state) SHALL keep the same
hue in both themes, so the same thing reads as the same colour after a switch.

#### Scenario: Timeline in the dark theme
- **WHEN** the monitor shows a turn's timeline in the dark theme
- **THEN** every kind's bar and badge meets the contrast above, and each kind has the same hue as in the light theme

#### Scenario: A degraded service
- **WHEN** Topology shows a degraded service in the light theme
- **THEN** its state text has at least 4.5:1 contrast with the surface

#### Scenario: Approving in the dark theme
- **WHEN** a confirmation card is shown in the dark theme
- **THEN** the Approve button's label has at least 4.5:1 contrast with its fill

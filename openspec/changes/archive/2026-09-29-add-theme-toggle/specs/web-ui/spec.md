# Spec Delta

## ADDED Requirements

### Requirement: The user can choose the theme
The header SHALL offer a theme button with three modes:
- **System**, the default, which follows the operating system's light or dark setting, including a change made while
  the app is open;
- **Light**;
- **Dark**.

Each press SHALL move to the next mode, in the order System, Light, Dark, then System again. The button SHALL say
which mode is on, in text and in its accessible name, not only with an icon.

**Remembering the choice.**
- The choice SHALL be remembered in the browser and applied on the next visit before the first paint, so the other
  theme is never shown on load.
- Choosing System SHALL forget the stored choice.
- When the browser does not allow storage, the choice SHALL still apply to the open page.
- The choice SHALL NOT be sent to the server.

**What it changes.** Every screen SHALL use the chosen theme. This includes native controls and the time-travel
banner.

#### Scenario: Forcing dark on a light system
- **WHEN** the operating system is light and the user presses the theme button until it says Dark
- **THEN** every screen is dark, and after a reload it is still dark, with no light frame first

#### Scenario: Back to the system
- **WHEN** the user presses the button until it says System
- **THEN** the app follows the operating system again, and no choice is stored

#### Scenario: The system changes while open
- **WHEN** the mode is System and the operating system switches from light to dark
- **THEN** the app turns dark without a reload

#### Scenario: Storage blocked
- **WHEN** the browser refuses storage and the user chooses Light
- **THEN** the page is light, and nothing fails

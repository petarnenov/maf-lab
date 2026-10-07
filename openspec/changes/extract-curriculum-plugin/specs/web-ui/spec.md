# Spec Delta

## MODIFIED Requirements

### Requirement: Curriculum map screen
The `/curriculum` screen SHALL explain where the concepts and rules of the 5-day Fullstack AI Engineer study plan
(revision 7) are applied in the lab while the curriculum plugin is in use. It SHALL be reachable from the main
navigation then; without the plugin, neither the screen nor its link exists.

The screen SHALL group its entries by the plan's days, in the plan's order, followed by a section for the plan's
rules. Each entry SHALL give:
- the concept's name;
- a short explanation, of two or three sentences, of how the lab applies it;
- the repository paths that implement it, the core's only (never a path into a plugin's folder);
- the spec that states it;
- a link to the screen where it can be seen, when such a screen exists.

The screen SHALL end with a section naming the plan's topics the lab does not implement, each with its reason, so the
page never claims coverage the code does not have.

The screen SHALL render from content shipped with the web app. It SHALL NOT call the api and SHALL NOT require a
session or a dev persona.

#### Scenario: Entries grouped by day
- **WHEN** the user opens `/curriculum`
- **THEN** one labelled section per day of the plan appears in the plan's order, then the rules section, then the not-covered section

#### Scenario: An entry names where to look
- **WHEN** the user reads an entry
- **THEN** it shows the explanation, at least one repository path and the spec name
- **AND** when the concept is visible in the app, a link opens that screen

#### Scenario: Readable without a persona
- **WHEN** no dev persona is picked
- **THEN** the whole page renders and no api request is made

#### Scenario: Honest about gaps
- **WHEN** the user reaches the end of the page
- **THEN** each topic of the plan that the lab does not implement is listed with its reason

#### Scenario: Without the plugin
- **WHEN** the curriculum plugin is not in use
- **THEN** the main navigation has no Curriculum link, and `/curriculum` lands on the chat like any unknown route

### Requirement: The header stays in view while the page scrolls
The app's header (brand, main navigation, persona picker and theme button) SHALL stay at the top of the window while
the page scrolls vertically, on every screen and in both themes. It SHALL keep an opaque background and its bottom
border, so page content scrolling under it is not visible through it. It SHALL wrap to as many rows as the window
width needs, and the behaviour SHALL follow its real height at any width, without assuming a fixed height.

It SHALL show above page content (tooltips, menus, sticky table columns) and below the app's overlays (dialogs, the
chat history drawer and its backdrop). In-page jumps (a link to an `#anchor`, such as the section links of a long page
like the Curriculum screen, and an element scrolled into view) SHALL bring their target to rest below the header, not
under it. A screen that sizes itself to the window SHALL fit below the header without making the window scroll as well.

When the header would take more than a third of the window's height, it SHALL NOT stay in view and SHALL scroll with
the page, so it does not take most of a small screen. It SHALL NOT stay in view when the page is printed.

#### Scenario: Long page
- **WHEN** a user scrolls down a long page, such as the Curriculum screen, in a 1440×900 window
- **THEN** the header with the main navigation, the persona picker and the theme button stays at the top of the window,
  and the cards scroll under it without showing through

#### Scenario: Section link lands below the header
- **WHEN** a user clicks a section link of a long page, such as "Not covered" on the Curriculum screen
- **THEN** the page scrolls so that section's heading is fully visible just below the header

#### Scenario: Wrapped header
- **WHEN** the window is 800 pixels wide and 900 tall, so the header wraps to several rows
- **THEN** the whole wrapped header stays at the top while the page scrolls, and section links land below all its rows

#### Scenario: Both themes
- **WHEN** the theme is switched between Light and Dark while the page is scrolled
- **THEN** the header stays in view with the theme's surface colour and border

#### Scenario: Small window
- **WHEN** the window is 375×667, where the wrapped header is taller than a third of the window
- **THEN** the header scrolls away with the page, and section links land at the top of the window

#### Scenario: Overlays cover the header
- **WHEN** the chat history drawer or a dialog is open
- **THEN** it and its backdrop show above the header

#### Scenario: Chat fits below the header
- **WHEN** the chat screen is open in a 1440×900 window
- **THEN** its panes fit between the header and the bottom of the window, and the window itself does not scroll

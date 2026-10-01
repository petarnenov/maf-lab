## ADDED Requirements

### Requirement: The header stays in view while the page scrolls
The app's header (brand, main navigation, persona picker and theme button) SHALL stay at the top of the window while
the page scrolls vertically, on every screen and in both themes. It SHALL keep an opaque background and its bottom
border, so page content scrolling under it is not visible through it. It SHALL wrap to as many rows as the window
width needs, and the behaviour SHALL follow its real height at any width, without assuming a fixed height.

It SHALL show above page content (tooltips, menus, sticky table columns) and below the app's overlays (dialogs, the
chat history drawer and its backdrop). In-page jumps (a link to an `#anchor`, such as the Curriculum section links,
and an element scrolled into view) SHALL bring their target to rest below the header, not under it. A screen that sizes
itself to the window SHALL fit below the header without making the window scroll as well.

When the header would take more than a third of the window's height, it SHALL NOT stay in view and SHALL scroll with
the page, so it does not take most of a small screen. It SHALL NOT stay in view when the page is printed.

#### Scenario: Long page
- **WHEN** a user scrolls down the Curriculum screen in a 1440×900 window
- **THEN** the header with the main navigation, the persona picker and the theme button stays at the top of the window,
  and the cards scroll under it without showing through

#### Scenario: Section link lands below the header
- **WHEN** a user clicks "Not covered" in the Curriculum section links
- **THEN** the page scrolls so the "Not covered" heading is fully visible just below the header

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

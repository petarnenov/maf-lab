# Spec Delta

## ADDED Requirements

### Requirement: The answer is rendered as safe markdown
The chat SHALL render the assistant's answer as GitHub-flavoured markdown. This covers:
- paragraphs, emphasis and strong emphasis;
- ordered and unordered lists;
- inline code and code blocks;
- tables;
- links.

It SHALL do so while the answer streams, when a conversation is reopened, and when a turn is shown at an earlier step
in time travel. The user's messages and the reasoning block SHALL stay plain text.

**Nothing in an answer SHALL be able to run script, load a resource or navigate the page on its own:**
- Raw HTML SHALL NOT be interpreted; it SHALL be shown as text.
- A link SHALL be rendered only for the `http`, `https` and `mailto` schemes. It SHALL open in a new tab with
  `rel="noopener noreferrer"`.
- A link with any other scheme SHALL be rendered as plain text.
- An image SHALL NOT be loaded; its alt text SHALL be shown instead.

**Tables** SHALL align numbers to the right in tabular figures, and SHALL scroll inside the answer at phone width
without scrolling the page.

#### Scenario: A procedure with steps
- **WHEN** the answer contains `1. Open the failed run\n2. Assign the schedule` and `**FS-REQUIRED**`
- **THEN** the steps render as an ordered list and FS-REQUIRED renders in bold, with no `**` or `1.` visible as text

#### Scenario: A table in an answer
- **WHEN** the answer contains a GFM table
- **THEN** it renders as a table with a header row

#### Scenario: HTML in an answer
- **WHEN** the answer contains `<img src=x onerror=alert(1)>` or `<script>`
- **THEN** no element is created from it and the text is shown literally

#### Scenario: An unsafe link
- **WHEN** the answer contains `[click](javascript:alert(1))`
- **THEN** "click" is shown as plain text and no link is created

#### Scenario: A safe link
- **WHEN** the answer contains `[docs](https://example.com/x)`
- **THEN** it renders as a link that opens in a new tab with `rel="noopener noreferrer"`

#### Scenario: A half-streamed answer
- **WHEN** the answer so far ends in the middle of a list or table
- **THEN** what has arrived renders without error, and the rest completes as it streams

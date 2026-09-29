# Proposal

## Why

The model answers in markdown: bold, lists, sometimes a table or code. The chat shows it as raw text (`white-space:
pre-wrap`), so users read `**`, `|---|` and `1.` literally. Data cards (add-activity-cards) and system.v3 removed the
worst case, restated portfolio tables, but every other answer still carries raw markdown: procedures with numbered
steps, bold terms, the occasional billing table.

## What Changes

- The assistant's answer is rendered as GitHub-flavoured markdown:
  - paragraphs, emphasis and strong emphasis;
  - ordered and unordered lists;
  - inline code and code blocks;
  - tables;
  - links.
- **Safe by construction:**
  - raw HTML in the answer is not rendered; it shows as text;
  - links are allowed only for `http`, `https` and `mailto`. They open in a new tab with `rel="noopener noreferrer"`,
    and any other scheme renders as plain text;
  - images are not loaded; they render as their alt text.
- Tables look like the data cards: numbers right-aligned in tabular figures, and horizontal scroll inside the answer
  at phone width.
- The same rendering applies while the answer streams (a half-written table or list renders as far as it goes), when
  a conversation is reopened, and when a turn is replayed in time travel.
- The user's own messages, the reasoning block and the monitor stay plain text.
- Two packages are added, pinned exactly: `react-markdown` 10.1.0 and `remark-gfm` 4.0.1, recorded in DECISIONS.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `web-ui`: the assistant's answer is rendered as safe GFM markdown.

## Impact

- `web/package.json` and the lock file: two dependencies.
- New `web/src/chat/Markdown.tsx` with its CSS module.
- `web/src/chat/ChatPage.tsx`: the answer text uses it.
- Tests: Vitest for each element, the unsafe cases (HTML, `javascript:` links, images), streaming partial markdown,
  and the page.
- No server change.

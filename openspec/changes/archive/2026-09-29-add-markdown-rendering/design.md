# Design

## Context

- `AssistantBubble` in `web/src/chat/ChatPage.tsx` renders `<div className={styles.text}>{text}</div>` with
  `white-space: pre-wrap`. `text` is the live text, the rewound text (time travel) or the restored answer.
- The web app has no markdown library. Its dependencies are exact-pinned in `web/package.json`, and a package move
  needs a DECISIONS entry (CLAUDE.md).
- The cards (add-activity-cards) define the table look: tabular numbers, right-aligned numbers, horizontal scroll.

## Goals / Non-Goals

**Goals:** readable answers, with no new way for model output to affect the page.

**Non-Goals:**
- No syntax highlighting.
- No math.
- No HTML passthrough.
- No markdown in user messages or in the monitor.

## Decisions

1. **`react-markdown` 10.1.0 + `remark-gfm` 4.0.1.**
   - react-markdown renders to React elements, never `dangerouslySetInnerHTML`. Without `rehype-raw` it does not
     interpret HTML.
   - GFM (tables, strikethrough, autolinks) comes from remark-gfm.
   - *Alternative:* a small in-house parser. Rejected by the owner in favour of complete, maintained GFM.
2. **One component, `Markdown`, with a locked-down config:**
   - `skipHtml` is false, so HTML stays visible as text. react-markdown escapes it by default.
   - `urlTransform` keeps `http:`, `https:` and `mailto:`, and returns `null` otherwise.
   - A `components.a` override renders a link without `href` as a `<span>`, and adds `target="_blank"` and
     `rel="noopener noreferrer"` to the rest.
   - `components.img` renders the alt text in a `<span>`.
   - `components.table` wraps the table in a scroll box. Number cells are right-aligned by a small check: a cell whose
     text parses as a number, a currency amount or a percentage.
3. **Streaming** needs nothing special. react-markdown re-parses the growing text on each render; the partial
   constructs of GFM parse to what they are so far. Answers are short (a few KB), so the per-token cost is negligible.
   If profiling ever says otherwise, memoise by text.
4. **Styling:**
   - `Markdown.module.css` keeps paragraph spacing tight inside the bubble and gives lists normal indentation.
   - Code uses the app's surface-2 token with monospace.
   - Tables use the card table rules (tabular-nums, borders from the tokens).
   - The bubble's `pre-wrap` moves to code blocks only.

## Risks / Trade-offs

- [A new dependency surface: react-markdown pulls in the unified/remark/mdast stack] → Pinned exactly, like the other
  dependencies, and recorded in DECISIONS with its install size.
- [Model output that looks like markdown but is not meant as markdown, e.g. `*` in a formula] → Acceptable. The answers
  here are prose and procedures.

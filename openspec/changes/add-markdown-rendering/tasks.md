# Tasks

## 1. The component

- [x] 1.1 Add `react-markdown` 10.1.0 and `remark-gfm` 4.0.1 to `web/package.json`, exact-pinned, and update the lock file. Verify that `npm ci` and `npm run build` pass.
- [x] 1.2 Add `web/src/chat/Markdown.tsx` and `Markdown.module.css` with the locked-down config (design §2). Verify with Vitest:
  - ordered and unordered lists, bold, inline and block code, and a GFM table render as elements;
  - `<script>` and `<img onerror>` show as text and create no element;
  - a `javascript:` link renders as plain text;
  - an `https:` link gets `target="_blank"` and `rel="noopener noreferrer"`;
  - an image renders as its alt text;
  - a half-written table and a half-written list render without throwing.

## 2. The chat

- [x] 2.1 Render the answer through `Markdown` in `AssistantBubble` for live, restored and rewound text; keep user messages and reasoning plain. Verify with `ChatPage` tests that a streamed answer with `**bold**` and a list shows no `**`, and that a restored answer renders the same way.

## 3. Verification

- [x] 3.1 Run `make lint`, `make test-web` and `make build-web`; all green.
- [x] 3.2 Rebuild with `make`. At http://localhost:7171/chat, ask "What is the procedure when a fee schedule is missing?" and verify the steps show as a list with bold terms and no raw markdown, and that at phone width nothing scrolls the page sideways beyond what the header already does.
- [x] 3.3 Add a DECISIONS entry (the packages, sizes, safety config, rejected alternative), then run `openspec validate add-markdown-rendering --strict`; valid.

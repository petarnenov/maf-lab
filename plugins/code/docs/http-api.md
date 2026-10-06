For any authenticated role.

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/code/snippets` | `{ question, maxResults? }` | `CodeSearchResult`; `400` without a question; `503` when code search cannot answer |

The Code snippets tab: `search_codebase` on the codebase MCP server, called with the caller's own bearer token, so the
server derives the principal itself. `maxResults` defaults to 8 and is clamped to 1–10.
`CodeSearchResult` = `{ results: [{ path, startLine?, endLine?, symbol?, section, kind, language, score, snippet }],
totalMatches, truncated, refineHint? }` — snippets only, never a synthesized answer. A chunk longer than
`CodeSearch:SnippetMaxChars` (1200) comes back as the window of whole lines around the lines that match the query's
terms, with `…` where lines were cut, and `startLine`/`endLine` are the window's; a query with no matching line gets the
chunk's first lines.

# Design

## Context

- A turn classifies first (`JevIntentClassifier`: one request carrying the intent Choice, one Noul per domain, the
  prompt-screening battery and the routing questions). Only then does it read tools (`IToolSource.GetToolsAsync`). So
  the domain verdict exists before any MCP connection is opened.
- Domains are already generic in most of the code:
  - `Domains.SearchTool`, `DomainQuestionIds` and `DomainVerdict.From` iterate over maps;
  - `ForcedSearches` maps domains in scope to their search tool;
  - `McpToolSource` merges any number of servers, each tagged with its domain.

  The places that still name billing and portfolio explicitly are the ones this change touches: the out-of-scope
  reply, the feedback resolver, the eval labels and the web colours.
- `search_codebase` returns `CodeSearchResult` (`path`, `startLine`, `endLine`, `symbol`, `section`, `kind`,
  `language`, `snippet`), not `SearchDocumentsResult` (`docId`, `sectionPath`, `sourcePath`, `snippet`). Sources,
  the answer check's reading and the guard's excerpts parse the latter.

## Goals / Non-Goals

**Goals:**
- A code question gets a grounded chat answer, and the Code snippets tab shows exactly what that answer used.
- A turn offers only the tools of the domains it is about, and a follow-up keeps its conversation's domains.
- No change to how a billing or portfolio question is classified, forced or answered, beyond fewer offered tools.

**Non-Goals:**
- `ask_codebase` stays an MCP tool for other clients. The agent writes its own answer.
- The topology screen's per-domain probe stays portfolio-only.
- No per-tool gating inside a domain: a domain is loaded whole (minus a server's allow-list).

## Decisions

### Jev: one more Noul in the existing request
- **State**: unchanged, `{ user_question }`. That is the same `JevState(question)` the other domain Nouls read.
- **Question** `in_codebase` (Noul), through `JevDomainInstructions`, the same shape as billing and portfolio:
  - `domain`: "The maf-lab software system itself: its source code, classes, methods, files and folders, tests,
    configuration, build and make targets, MCP servers and their tools, the agent, the chat UI, OpenSpec
    specifications and design decisions — how this system is built and where something is implemented. General
    programming questions that are not about this system are not in it."
  - `languages`: the same note as the other domains.
  - `question`: "Is `user_question` about something in `domain`?"
- **Why Jev and not code or an LLM** (rule §2, §5):
  - It is a closed yes/no judgment.
  - It needs language understanding, since "идемпотентност на тул" and "where is the tenant filter built" share no
    keyword.
  - Code acts on the probability.
  - It rides in the request the turn already makes, so it costs no round trip.
  - A keyword list would miss the Bulgarian, and an LLM call would add seconds.
- **Thresholds**: the existing ones (`MinInDomain` gate 0.2, `MinDomainScope` 0.5).
  - The risk is read-only. A wrong "in scope" only offers a search tool, and the answer still rests on retrieved
    snippets that the relevance gate filters.
  - The domain suite's floor sweep reports how the codebase cases fare at 0.3–0.8. The floors move only if the eval
    says so.
- **Fallback**: unchanged. No answer means no verdict, which means every server is loaded and nothing is forced.
- **Model**: pinned `jev-1.13.0`, as configured, and logged per call as today.
- **Review band**: the gate/scope pair is the existing band. Between 0.2 and 0.5 the most probable domain alone is in
  scope.

### Forcing for codebase questions
- `ForcedSearches` is unchanged for procedural and mixed intents: every domain in scope, the codebase included.
- New: when the primary domain in scope is the codebase and the intent is not chitchat, `search_codebase` is forced
  even for data or other. Code questions often read as "show me X" (data) or match no intent (other), and without the
  search the model has nothing to answer from.
- The forced call goes through the existing emulated required-tool path (`RequiredToolModeChatClient` already accepts
  any `Domains.IsSearch` tool).

### Tool selection by conversation domains
- `IToolSource.GetToolsAsync(bearer, confirmations, ct, IReadOnlySet<string>? domains = null)`: null means every server.
  Confirmation calls keep null.
- `ChatTurnRunner.SelectDomains(decision, stored)` gives `(domains, reason)`:
  - `InScope` if it has any → `in scope`;
  - else the stored domains, when the decision has a verdict and there are any stored → `conversation`;
  - else null → `all`.
- `ConversationRow.Domains` (a comma list, nullable) is written when a turn has domains in scope. It is added by the
  existing additive-column initializer. This mirrors `FocusAccountId`, the conversation-level state that lives there.
- The billing server is the "first" server only when it is selected. A turn that did not select billing does not fail
  when billing is down.
- `McpServerOptions.Tools` is an allow-list; empty means all. Compose sets `Agent__Servers__1__Tools__0:
  search_codebase`.

### Sources carry code fields
- `SourceRef` gains optional `Kind`, `StartLine`, `EndLine`, `Symbol` and `Language`. Records with defaults keep
  every existing constructor call valid, and stored JSON without them reads as before.
- For code: `DocId` = path, `SectionPath` = `path:start-end > symbol`, `SourcePath` = path.
- One reader, `SourceRef.FromSearchItem(JsonElement)`, gives the source with its code fields for either
  result shape. `Summarise` (sources), `Read` (answer check) and history use it. The guard already reads `snippet`.
- The review queue resolves chunk ids only for billing and portfolio searches. A code search's `ChunkIds` stay empty.

### system.v4
- v3 plus a Codebase tools section and one example.
- Scope: this lab's own code is in scope; general programming is out.
- Rule: for code, cite `path:start-end` from the snippet, and never invent a path or a line.
- `Agent:SystemPrompt=system.v3` rolls back.

### Web
- The Code snippets tab decides its content from the selected turn:
  - `turn.sources.filter(kind === 'code')` present → "Used in this answer · N", with no fetch;
  - else the existing related-code query, labelled "Related code — not used by this answer".
- `ChatPage` holds `paneTab` plus `highlight: string | null`. An effect switches to `code` once, when the streaming
  turn gains code sources, keyed by turn id so a later manual switch stands.
- `SourcesPanel` takes an optional `onOpenCode(source)`. A code source renders as a button (`path:start-end ·
  symbol`), and choosing it sets the tab and the highlight. `CodeSnippetsPanel` scrolls the highlighted snippet into
  view and outlines it.

## Risks / Trade-offs

- [A billing question with a code flavour ("how does the code compute a tiered fee?") is in both billing and the
  codebase] → Both searches are forced, as with any crossing. The answer names which part came from where. That is
  the intended behaviour for a crossing.
- [A follow-up after a code question that is really about billing, with no domain words] → It keeps the code tools
  for that turn. The next question with domain words reselects. The trace says `conversation`, so it is visible.
- [Fewer offered tools could change selection on edge cases the eval does not hold] → The `selection` suite reruns
  with the new system prompt and tool gating, compared with its baseline before merge.
- [One more Noul changes the other Nouls' answers?] → Jev answers each question independently over the same state
  (rule §1), and the intent and domain suites rerun to confirm.

## Migration Plan

- The additive column is created on startup, and existing conversations have `Domains` null.
- The api gets `Agent__Servers__1__Domain=codebase`, `…Endpoint=http://lb/code/mcp` and
  `…Tools__0=search_codebase`. Without that server configured, the codebase domain is simply never loaded.
- Rollback:
  - `Agent:SystemPrompt=system.v3`;
  - remove `Agent__Servers__1__*`.

  The Noul stays harmless: a codebase domain with no server offers no tools.

# Proposal

## Why

A question about the lab's own code gets two contradicting answers on the same screen:
- The chat refuses: "I can only help with billing and portfolios". Jev puts the question outside every domain, and
  the system prompt lists "coding" as out of scope.
- The **Code snippets** tab next to it fills with exactly the files that answer the question.

The codebase is searchable (add-codebase-search), but the agent does not know it. The user reads a refusal beside the
answer.

A related problem: every turn is offered the tools of every server, whatever the question is about. A billing
question sees portfolio tools, and a code question would see billing's. More tools in the prompt means more chances
to pick the wrong one, more prompt tokens, and more MCP connections per turn. The classifier already decides which
domains a question belongs to, but tool loading ignores that decision.

## What Changes

- **The codebase becomes the agent's third domain.**
  - Jev gets a third yes/no domain question, `in_codebase`, in the same classification request. No extra request.
  - The agent reads the codebase server's tools like the other domains' tools, limited to `search_codebase`.
    `ask_codebase` writes its own answer and is not offered to an agent that writes one.
  - A question whose primary domain is the codebase forces `search_codebase` for any intent but chitchat. The
    codebase has no read tools, so its search is the only way to answer.
  - A procedural question in the codebase and another domain forces both searches, as crossings do today.
  - The fixed out-of-scope reply names the codebase among the things the assistant answers.
- **Tools are loaded by the conversation's domains.**
  - The tool source connects only to the servers of the domains the question is in.
  - A follow-up that Jev puts in no domain ("and for A-1043?", "show me more") keeps the domains of the conversation's
    last classified turn. They are stored with the conversation.
  - With no domain verdict (Jev down, no key) every server is loaded, as today.
  - The trace's `domain` event says which domains' tools were loaded, and why.
  - Confirmations still go to every server: a confirmation belongs to a proposal, not to a question.
- **System prompt `system.v4`.**
  - A Codebase section: `search_codebase`, and cite `path:start-end`.
  - Scope: this lab's own code is in scope, general programming is not.
  - `system.v3` stays for rollback.
- **Search results of the codebase are first-class sources.**
  - The sources of a turn carry `kind`, `startLine`, `endLine`, `symbol` and `language` when they are code.
  - The guard, the answer check and the stored history read code results as they read documentation results.
  - The review queue does not try to resolve code sources in the billing collection.
- **Web: the Code snippets tab follows the answer.**
  - A turn that searched the codebase shows the snippets **the answer used**, labelled so.
  - The pane switches to Code snippets by itself when such a turn streams. The tab shows the number of snippets.
  - A code source in the answer's Sources opens the tab on that snippet and highlights it.
  - A turn that did not search the code still shows **related code** for its question, as today, labelled as not used
    by the answer.
  - The tool card reads "Searched the codebase", and the Domains view lists the codebase.
- **Evals.**
  - The domain dataset gains codebase questions (English, Bulgarian, Latin-script Bulgarian).
  - The domain label vocabulary becomes the set of domains in scope.
  - The selection dataset gains codebase cases.
  - The eval host loads the codebase server.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `intent-classification`: a domain question for the codebase; codebase questions force `search_codebase`.
- `chat-agent`: tools loaded by the conversation's domains; the codebase server's search offered; out-of-scope reply.
- `turn-tracing`: the `domain` event records the domains whose tools were loaded and why.
- `web-ui`: the Code snippets tab shows the snippets the answer used, switches by itself, links from Sources; the
  codebase in tool labels and the Domains view.
- `codebase-search`: search results carry what the chat's sources need; `search_codebase` is offered to the agent.
- `eval-harness`: codebase cases in the domain and selection suites, and multi-domain labels.

## Impact

- `Maf.Lab.Api`:
  - `Domains` (Codebase);
  - `JevIntentClassifier` (the `in_codebase` Noul);
  - `DomainVerdict` users;
  - `ChatTurnRunner` (forcing, tool selection, conversation domains, sources, trace);
  - `IToolSource` (a domain filter);
  - `McpServerOptions.Tools` (allow-list);
  - `OutOfScope`;
  - `Prompts/system.v4.md`;
  - `FeedbackEndpoints`, `HistoryEndpoints`;
  - `ConversationRow.Domains` (an additive column).
- `Maf.Lab.Domain`: `SourceRef` optional code fields.
- `Maf.Lab.Eval`: datasets, metrics and the host (the code endpoint). Makefile `EVAL_HOST`.
- Compose, `make dev` and `appsettings.json`: the api's `Agent__Servers__1__*` for the codebase server.
- `web/`:
  - the Code snippets panel and `ChatPage`;
  - `SourcesPanel`, tool labels and domain colours.
- The system prompt, the tool set and the Jev request change, so the `selection`, `domain` and `intent` suites must be
  rerun and compared with their baselines.
- Jev: one more Noul in an existing request. The same state, and no new request.

# Design

## Context

These decisions were taken with the user on 2026-10-05 and moved here from `introduce-plugins` to keep that change
smaller (re-review, M4). This change needs `introduce-plugins`, which provides the plugin contract and the `provider`
kind.

Today Jev is called directly (`JevClient`, `JevIntentClassifier`, `JevGuard`, `JevAnswerCheck`). The chat model and
embeddings already go through `Microsoft.Extensions.AI` (`IChatClientFactory`, `ModelProviders.cs`). Embeddings reach
`ollama-batch` with its `num_thread` for documents and `ollama` for queries (`ModelProviders.cs:105-118`); that path
stays exactly as it is.

## Decisions

### 5t. Typed decisions behind an abstraction; Jev is the first engine (decided with the user, 2026-10-05)

The core no longer depends on TypeSafe Jev directly. It depends on `IDecisionEngine`, which answers closed, typed
questions over one state with a confidence for each answer. The core puts all the questions over one state to it in
one request (jev-usage §0.4): a turn asks once per state, as before (its question, each tool result, its answer).

**Engines are provider plugins.** A plugin of the new kind `provider` declares `provides = "decision-engine"`. Exactly
one engine must be installed, or make and the api refuse to start, naming the problem. Two engines are planned:
- `jev`, the TypeSafe Jev client as it is today;
- a local engine on a local model, for a customer who allows no external subprocessor.

**`jev-usage.md` becomes the contract of `IDecisionEngine`, binding on any engine:**
- closed answer spaces only, one atomic question each;
- all questions over one state in one request;
- no math, dates or counting;
- confidence gated by risk;
- never a security boundary.

**Enforcement.**
- One contract suite runs against every engine: the same questions and the same expected decisions.
- Before an engine is used in stage or prod, `make eval SUITE=selection` must hold the accepted baseline with it.

**The rest of the system moves too:**
- `JEV_MAF_LAB` and the `jev-1.13.0` pin move into the `jev` plugin.
- The rules in CLAUDE.md and project.md that name Jev as "the only" classifier are restated as "the installed decision
  engine; Jev today".
- The relevance judge in mcp-retrieval belongs to the billing domain's server. It keeps its own engine choice behind
  the same abstraction.


### 5u. Chat models are provider plugins (decided with the user, 2026-10-05)

**Providers are plugins.** Each model provider is a `provider` plugin with `provides = "chat-model"`:
`ollama-cloud` (today's `gpt-oss:120b`), `azure-openai`, `local-ollama`, and others as they are needed. The core reaches
a model only through `Microsoft.Extensions.AI`'s `IChatClient`, as today.

**Choosing one.** An installation may install several providers. `MAF_CHAT_MODEL` names the one the chat agent uses.
make and the api refuse to start when it names a provider that is not installed. There is no choice per tenant and no
customer keys (BYOK) in this change: per-tenant secrets need a vault and rotation of their own, which is a later change.

**Embeddings follow the same pattern** (`provides = "embeddings"`), with today's local `embeddinggemma` as the first
provider. Changing it still means a new profile and a full reindex, as today.

**Promotion rule.** A provider or model change reaches stage or prod only after `make eval` holds the accepted baselines
with it. The assistant's behaviour depends on the model.

**Migration sequence (user decision, 2026-10-08).** Complete the entire agreed code migration first, using builds,
contract tests and docs/spec checks during that phase. Indexing, graph refreshes and index-backed live evals are a
final validation phase after the code migration is complete; they do not block the remaining code changes. Task 1.5
tracks this deferred gate. The promotion rule still applies before stage/prod.


### 5x. The core's minimum providers (review finding 1)

"No plugin" cannot mean "no provider". The core needs one decision engine (5t) and one chat model (5u), and some
domains need stores (5g). So the core's minimum is the core services plus exactly the provider and infra plugins the
deployment names:

```
MAF_CORE_PROVIDERS ?= jev ollama-cloud ollama-embeddings     # dev default; stage and prod name their own
```

`make core` installs those and no domain, app or dev plugin. It starts healthy and declines every turn (5h). The
core-only CI leg (task 7.1) asserts exactly that: a turn ends with the decline, and no model, decision-engine or tool
call is made.

A domain then brings its stores through `depends`. Installing `billing` pulls in `qdrant` and `neo4j`, and requires an
`embeddings` provider.


## Risks / Trade-offs

- [The abstraction changes routing behaviour] → the contract suite and `make eval SUITE=selection` must hold the
  baseline through the `jev` engine before anything else moves.
- [An embeddings provider loses the per-instance `num_thread`] → the `ollama-embeddings` provider keeps today's two
  instances and their `num_thread` (DECISIONS §77). A test asserts every request carries it.

# Proposal

## Why

The core of a universal assistant must not be bound to one vendor for typed decisions (TypeSafe Jev) or for models
(Ollama Cloud, local Ollama). Each is a subprocessor or a cost a customer may refuse. The user decided on 2026-10-05:
- Jev goes behind an abstraction and can be replaced by a local model (5t);
- chat models and embeddings are provider plugins (5u).

This change was split out of `introduce-plugins` to keep that one reviewable.

## What Changes

- **`IDecisionEngine`.** Closed, typed questions go in, and typed answers with confidence come out, one request per
  state. `docs/rules/jev-usage.md` becomes its contract. The Jev client becomes the `jev` provider plugin
  (`provides = "decision-engine"`). Exactly one must be installed.
- **Chat-model and embeddings providers** over `IChatClient` and `IEmbeddingGenerator`:
  - `ollama-cloud` (today's `gpt-oss:120b`);
  - `ollama-embeddings` (today's `embeddinggemma` on the two local instances, unchanged).

  `MAF_CHAT_MODEL` names the chat provider.
- **`MAF_CORE_PROVIDERS`** names the minimum that `make core` installs. make and the api refuse to start without exactly
  one decision engine and the named chat provider.
- **Every provider must pass a contract suite**, and a provider change reaches stage or prod only when the eval
  baselines hold.
- **Input tokens are measured.** The decision request's input tokens per turn are recorded by the number of domains in
  use (`introduce-plugins` 5k).

## Capabilities

### New Capabilities

- `plugin-providers`: the decision-engine, chat-model and embeddings providers, their cardinality, their contract
  suites, and the promotion rule.

### Modified Capabilities

- `make-workflow`: a new requirement, the core's minimum providers. `make core` from `introduce-plugins` now installs
  `MAF_CORE_PROVIDERS`.

## Principles

- SOLID: dependency inversion is the point. The core depends on `IDecisionEngine`, `IChatClient` and
  `IEmbeddingGenerator`, never on Jev or Ollama. Interface segregation: three small contracts, not one "AI provider".
- Standards: `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`), the project's existing abstraction; the
  provider pattern through the plugin contract.
- Own: `IDecisionEngine`. No established abstraction exists for typed, closed-set decisions with confidence. Its
  contract is `docs/rules/jev-usage.md`. DECISIONS §82 (new, written when this change is applied).

## Progress

None — this change adds no long work; the decision, chat and embedding calls keep the progress their callers already
show.

## Stopping

None — no new work starts here. Every call keeps the cancellation path it has today, which is the run's or the index
run's `CancellationToken`, passed into the provider.

## Documentation impact

- `CLAUDE.md` and `openspec/project.md`:
  - Jev is restated as the first `IDecisionEngine` (the `jev` provider), no longer "the only" classifier;
  - the chat model and embeddings lines become "the installed `chat-model` / `embeddings` provider; today
    `ollama-cloud` with `gpt-oss:120b` and local `embeddinggemma`".
- `docs/plugins.md`: the provider kinds.
- DECISIONS §82 (new).

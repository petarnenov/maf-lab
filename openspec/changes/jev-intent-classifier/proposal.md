# Proposal

## Why

The second stage of intent classification asks a chat model for one word. It is the only model call in a turn
whose whole output is a pick from five known labels, yet it pays a chat model's price for it: `gemma4:31b` costs
a median 479 ms (DECISIONS.md), and every Bulgarian question reaches it because the rules are English. It also
cannot say how sure it is — a guess and a certainty arrive as the same word.

TypeSafe's Jev (public since 2026-09-15) is built for exactly this shape: a question with a fixed answer space,
answered with a label, a probability for every option and a confidence, and never anything outside the schema.
Published figures put a three-question call at ~300 ms. This change tries it in the one place it fits best, behind
configuration, and keeps it only if the evals say so.

## What Changes

- A new, optional judge for the model stage of intent classification: a **Choice** question over the five intents
  (Procedural, Mixed, Data, ChitChat, Other), sent to Jev.
- **Confidence becomes part of acceptance.** A Jev answer counts only when its confidence reaches a configured
  threshold. Below it, the answer is discarded exactly like an unrecognised one today.
- **Fallback to the current classifier.** When Jev is not configured, fails, times out or is not confident enough,
  the existing chat-model classifier decides, within the **same** overall timeout — the turn never waits longer
  than it does today.
- **Off by default.** The provider is chosen by configuration (`Agent:IntentProvider`, `model` | `jev`); `model`
  stays the default, so CI, `make`, and any environment without a Jev key behave exactly as now. Choosing `jev`
  without a key refuses to start rather than silently falling back forever.
- **Only the question leaves the system.** The request to Jev carries the question text and the fixed label
  descriptions — never the firm, the principal, history, or retrieved content.
- **Trace records the judge.** The intent event names which judge decided (rules, Jev, or model), and Jev's
  confidence and per-label probabilities when it was asked.
- **Evals that actually reach the stage.** `evals/selection.jsonl` has 24 cases, all English, so the rules decide
  every one and the model stage is never measured. The change adds non-English and rule-miss cases so that
  `make eval SUITE=selection` exercises it, and records a baseline for both providers.
- Jev is adopted only if, on that dataset, it holds recall, precision, exactMatch and negativeAccuracy at the
  model provider's values and is faster. If it does not, the result is recorded and the default stays `model`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `chat-agent`: "Conditional forced retrieval" — the model stage may be answered by a judge that reports
  confidence; a low-confidence answer is treated as unrecognised; a failed or unsure first judge falls back to the
  chat-model classifier within the same timeout; only the question text may be sent to an external judge.
- `turn-tracing`: "Complete turn trace" — the intent event names which judge decided and, for a judge that reports
  it, its confidence and per-intent probabilities.

## Impact

- `src/Maf.Lab.Api/Agent/` — `ModelIntentClassifier` split into a stage runner and judges; a Jev judge with its
  own typed `HttpClient`; `AgentOptions` gains provider, threshold and Jev settings; `IntentDecision` gains
  confidence.
- `src/Maf.Lab.Api/Program.cs`, `src/Maf.Lab.Eval/Hosting/EvalAgentHost.cs` — registration by provider.
- `compose/docker-compose.yml` — `INTENT_PROVIDER`, `TYPESAFE_API_KEY` (empty by default).
- `compose/ollama-stub/server.py` — a Jev-shaped endpoint so `make ci-e2e` can exercise the `jev` provider without
  a key.
- `evals/selection.jsonl`, `evals/baseline.json` — new cases; baseline re-accepted.
- `web/` — the behind-the-scenes intent row shows the judge and confidence.
- `DECISIONS.md` — the provider choice, the threshold, and the numbers behind them.
- **New external service**: TypeSafe API (question text only). No new NuGet package — a thin typed client over
  `HttpClient`, since only community .NET SDKs exist.
- Requires access to the TypeSafe Console (API key). The request/response shape is taken from secondary sources
  and must be confirmed against the official API reference before the client is written (tasks 1.x).

# Proposal

## Why

"Procedurata kak edna vaba da izqden edin slon e: ???" ("the procedure by which a frog eats an elephant") was classified
`procedural` at 0.93, forced `search_documents`, found nothing, answered with a refusal, and was flagged
`zero_retrieval_results` for review. Jev was not wrong: the question *is* phrased as a procedure. The defect is the
question we ask it. The intent Choice judges only the **form** of the answer a question needs; nothing asks whether the
question is about the domain our documentation covers. Two judgments are hidden in one question — exactly what
TypeSafe's guidance says not to do.

Measured on 101 labelled questions (71 used for design, 30 held out) in English, Bulgarian and Bulgarian written in
Latin letters: the current classifier forced retrieval on **21 of 21** procedurally-phrased off-domain questions in the
design set and gets **67–68/101** forcing decisions right overall (two runs). No eval caught it, because `selection.jsonl` contains
only in-domain questions — and real traffic already contains the other kind (the retrieval eval's off-domain cases —
"What is JWE?", "why doesn't it autocomplete" — came from production traces).

## What Changes

- The intent call to Jev asks a second, atomic question in the same request: a Noul, "is `user_question` about
  something in `domain`?", with the billing domain described once in the question's structured instructions.
  Code combines the two: retrieval is forced only when the intent is procedural or mixed **and** the in-domain
  probability reaches a configured floor (0.2).
- A procedurally-phrased question outside the domain proceeds with no recognised intent (`Other`) and a reason — so it
  neither forces a search nor raises "how/why answered without a tool" or "zero retrieval results" in the review queue.
- The `intent` trace event records the in-domain probability next to the intent's choice, probabilities and confidence.
- A new eval suite, `intent`, measures the classifier directly — no chat model, no MCP — over a labelled dataset of the
  101 questions: forcing accuracy, false-force rate on off-domain questions, missed-force rate on in-domain ones, each
  also per language. It gates like the other suites, against a baseline.

Measured effect (probe, jev-1.13.0, two runs): **100/101** correct (was 67–68/101), **0** false forces (was 33–34),
held-out set 30/30 (was 17/30). One miss remains: "Kak se izdava kredit po smetka za taksi?" (in Latin script "taksi" reads as
"taxi"), which then goes unforced — the answering model can still search.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `intent-classification`: the classification request asks the domain question alongside the intent; a new requirement
  gates forced retrieval on the domain.
- `chat-agent`: "Intent-driven forced retrieval" forces only for procedural questions inside the domain, and gains a
  scenario for an off-domain procedural question.
- `turn-tracing`: the intent event carries the in-domain probability; a scenario for an off-domain question.
- `eval-harness`: a requirement that intent classification is measured directly, per language, including off-domain
  questions.

## Impact

- `src/Maf.Lab.Api/Agent/Jev/` — the request carries two questions; `JevOptions` gains `MinInDomain`; the decision
  gains `InDomain`.
- `src/Maf.Lab.Api/Agent/IntentClassifier.cs`, `ChatTurnRunner.cs` — decision shape and trace payload.
- `src/Maf.Lab.Eval/` — `IntentSuite`, dataset loader, registration in `Program.cs`, `eval.json` thresholds.
- `evals/intent.jsonl` (new), `evals/baseline.json` (new `intent` entry), `Makefile` (`eval-intent`),
  `.github/workflows/evals.yml` (suite option).
- `compose/ollama-stub/server.py`, `tests/Maf.Lab.Tests/FakeJev.cs` — answer the Noul.
- `DECISIONS.md` — the measurement and the threshold.
- Cost: one more question in the same call — median latency 306 ms with four questions vs ~285 ms with one; input
  tokens rise (≈1000 per call with four), billed at $0.042/Mtok.

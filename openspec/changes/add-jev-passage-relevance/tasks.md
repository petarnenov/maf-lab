# Tasks

## 1. Shared Jev plumbing

- [ ] 1.1 Move `JevCredential`, `JevAuthHandler`, `JevOptions` and the wire DTOs (`JevRequest` with an object state,
      `JevChoiceQuestion`, `JevNoulQuestion`, `JevResponse`, `JevAnswer`) to `src/Maf.Lab.Retrieval/Jev/`, with an
      idempotent `AddJevClient(services, configuration)` that builds the credential from the given configuration.
      Keep the intent-specific records (`JevState`, `JevDomainInstructions`) with the classifier. Verify `make lint`
      and the existing `IntentClassifierTests` pass unchanged in behaviour.

## 2. Relevance judgment

- [ ] 2.1 Add `RelevanceGateEnabled`, `RelevanceFloor`, `RelevanceCandidates`, `RelevanceTimeoutSeconds`,
      `RelevancePassageChars` and `Reranker` to `RetrievalOptions`; `RelevanceGate` and `Reranker` to `SearchSettings`.
- [ ] 2.2 Add `IRelevanceJudge` / `JevRelevanceJudge`: one Noul per candidate (`p0…`), state `{query, passages}`, bounded
      by the timeout race, failing open with a reason (timeout, rejected (status), no key, no answer, disabled). Verify
      unit tests with `FakeJev`: one request carrying one question per passage and none of the text in instructions;
      probabilities read back in order; timeout returns within budget with a hanging fake; 503 and missing key give
      reasons; logs hold neither query nor passage text nor the key.
- [ ] 2.3 Add `IReranker.Kind`; `JevReranker` ordering by probability with fused-rank tie-break and unjudged candidates
      after; register `llm` and `jev` rerankers. Verify unit tests for the order, the tie-break and the fail-open path.

## 3. The gate in the search path

- [ ] 3.1 In `DocumentSearchService.RankAsync`, obtain the judgment once when the gate is on or the Jev reranker is
      selected; silence the search when the maximum is below the floor; hand the same judgment to the Jev reranker.
      Record the `relevance` stage timing and a structured warning on failure. Verify a unit/integration test that a
      gated off-domain search returns no results and a refine hint, an answered one returns the fused order unchanged,
      and gate + Jev rerank make one request.
- [ ] 3.2 Diagnostics: `relevance` object (floor, judged count, max, silenced, per-chunk probabilities, model,
      duration, reason) and the effective gate/reranker in settings. Verify a traced search carries it.
- [ ] 3.3 Retrieval server: resolve the credential at startup (warn once); keep integration tests hermetic (no real
      Jev call when `JEV_MAF_LAB` is in the developer's environment). Verify `make test`.

## 4. Test doubles and CI

- [ ] 4.1 `FakeJev` answers passage requests (state with `query` and `passages`) with a settable per-passage function
      (default: word overlap), and still answers intent requests as before.
- [ ] 4.2 `compose/ollama-stub/server.py` answers passage Nouls: 0.0 when the query contains an off-domain stub word,
      1.0 otherwise; comment in `compose/docker-compose.yml` that the retrieval server uses the key too. Verify the
      stub with a local request (python) — the e2e itself runs after merge.

## 5. Eval

- [ ] 5.1 Retrieval suite: add the gate-flipped hybrid variant; with `--rerank`, `hybrid+rerank-llm` and
      `hybrid+rerank-jev`; collect relevance outcomes per variant and print failures apart from quality, and the
      highest off-domain / lowest in-domain maximum.

## 6. Measure and decide

- [ ] 6.1 Tune the floor from the real eval's maxima (production hybrid shortlists), then run `make eval
      SUITE=retrieval` ≥ 3 times with the gate on; apply the gate rule from design D7.
- [ ] 6.2 Run the retrieval eval with `--rerank` ≥ 3 times (llm vs jev vs none); apply the rerank rule from D7.
- [ ] 6.3 Set the defaults the rules decided; record probes, runs, timeouts and decisions in `DECISIONS.md`; accept the
      retrieval baseline only if the gate ships on.

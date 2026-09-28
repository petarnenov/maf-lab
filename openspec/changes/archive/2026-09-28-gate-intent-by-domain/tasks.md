# Tasks

## 1. Dataset first

- [x] 1.1 Create `evals/intent.jsonl` from the 101 labelled questions of the planning probe, kept in this change's
      `probe/design-set.jsonl` (71, `split: "design"`) and `probe/holdout-set.jsonl` (30, `split: "holdout"`), each with `id`, `question`, `forces`, `category` (in-proc, in-mixed, in-data, in-write,
      chitchat, off-proc, off-meta, off-trap, steer), `language` (`en`, `bg`, `bg-latn`) and `split`. Verify the file
      parses, ids are unique, and every language has both should-force and should-not-force cases.
- [x] 1.2 Add the dataset's loader to `src/Maf.Lab.Eval/Datasets/Datasets.cs` next to the existing ones. Verify a unit
      test loads the file and counts 101 cases, 53 should-not-force, 37 of them in the off-* and steer categories.

## 2. The domain question

- [x] 2.1 Add `MinInDomain` (default 0.2; 0 disables the gate) to `JevOptions`, and `InDomain` (double?) to
      `IntentDecision`. Verify `make lint` builds clean.
- [x] 2.2 In `JevIntentClassifier`, send the `in_domain` Noul from the design alongside the `intent` Choice in the same
      request (structured instructions: `domain`, `languages`, `question`), and extend the DTOs to read a Noul answer.
      Verify with `FakeJev` that one request carries both questions, that neither contains the user's question, and
      that the documented Noul response shape (`{"type":"noul","noul":0.95}`) is read.
- [x] 2.3 Apply the gate in `Decide`: for Procedural or Mixed only, an `in_domain` below `MinInDomain` or missing
      yields `Intent.Other` with reason `outside the domain (0.xx)`, keeping choice, probabilities, confidence and
      `InDomain`. Verify unit tests for: off-domain procedural → Other with reason; in-domain procedural → forced;
      data with in_domain 0 → still Data; missing in_domain → Other; `MinInDomain = 0` → gate off.
- [x] 2.4 Verify through `ApiFactory` that an off-domain procedural turn (FakeJev with in_domain 0.02) forces nothing
      and its signals contain neither `no_tool_on_how_why` nor `zero_retrieval_results`.

## 3. Trace

- [x] 3.1 Add `inDomain` to the `intent` event payload and to its title when present
      (`Intent Other (jev 0.93, outside the domain 0.02, 291 ms)`). Verify `TurnTraceTests` asserts it for an in-domain
      and an off-domain turn.

## 4. Test doubles

- [x] 4.1 `FakeJev` answers every Noul it is asked with a settable `InDomain` (default 1.0) and records the question
      ids it received. Verify `make test` passes.
- [x] 4.2 `compose/ollama-stub/server.py` answers Noul questions: 0.0 when the question contains one of a short
      off-domain word list (cook, recipe, carbonara, passport, weather, баница, паспорт), 1.0 otherwise. Verify the
      model-free e2e (`COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`) still passes.

## 5. The `intent` eval suite

- [x] 5.1 Add `IntentSuite` in `src/Maf.Lab.Eval/Suites/` that builds a service provider with only
      `AddJevIntentClassifier`, classifies every case, and reports `accuracy`, `unforcedWhenShouldNot`,
      `forcedWhenShould` and `accuracy:<language>`, with every wrong case named (question id, choice, confidence,
      in_domain). Fail fast when `JEV_MAF_LAB` is not set. Verify a unit test of the metric function over a small
      hand-made set gives the expected numbers.
- [x] 5.2 Register `intent` in `src/Maf.Lab.Eval/Program.cs` (and in `all`), thresholds in
      `src/Maf.Lab.Eval/eval.json` (0.95 each), `make eval-intent` in the Makefile, and the option in
      `.github/workflows/evals.yml`. Verify `make help` lists it and the workflow YAML parses.
- [x] 5.3 Run `make eval SUITE=intent` twice and confirm the two runs agree (Jev is designed to be self-consistent);
      then `make eval-accept SUITE=intent` and verify `evals/baseline.json` gains an `intent` entry and nothing else.

## 6. Verification

- [x] 6.1 Verify the `intent` suite reports accuracy ≥ 0.99 overall and 30/30 on the held-out split, with
      `unforcedWhenShouldNot` = 1.0; record the per-language numbers.
- [x] 6.2 Run `make eval SUITE=selection` three times and `SUITE=generation`, `SUITE=injection`. Verify no regression
      against the baseline — every in-domain case in those suites must still be forced as before.
- [x] 6.3 With the stack running, ask "Procedurata kak edna vaba da izqden edin slon e: ???". Verify the intent event
      shows choice procedural, in-domain ≈ 0.02, intent Other, nothing forced, and the turn's signals are empty.
- [x] 6.4 Update `DECISIONS.md` (§32 gains the domain gate: the measurement table, the 0.2 floor and why, the
      rejected variants) and the README's intent paragraph. Verify `make lint` and `make test` pass.

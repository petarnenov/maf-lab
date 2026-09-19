# Tasks

## 1. Measure first

- [x] 1.1 Add the Bulgarian twins of the existing retrieval cases to `evals/retrieval.jsonl` (same `relevantChunkIds`, new `language: "bg"`), teach the harness the optional field (absent = corpus language) and report `recallByLanguage` alongside the overall metrics; verify harness tests for a dataset with and without the field, and record the **before** numbers by running `make eval-retrieval` against the running stack

## 2. Normalise the query

- [x] 2.1 Add `IQueryTranslator` (no-op + LLM, mirroring `IReranker`/`LlmReranker`: own configured model, own timeout, failure degrades to the original query, no query text in logs) and its options (`Retrieval:NormalizeQueryLanguage` default on, `Retrieval:CorpusLanguage` default `en`, `Models:TranslationModel`, timeout, cache size); verify unit tests with a scripted model: Cyrillic query translated, Latin query never sent, identifiers preserved, failure/timeout/empty answer leaving the original, disabled switch, and the in-memory cache serving a repeated query once
- [x] 2.2 Call it in `DocumentSearchService.RankAsync` before embedding and BM25 encoding, and record `query.original`, `query.searched` and `query.translationMs` in `SearchDiagnostics`; verify tests that both branches use the translated text, that the tenant scope and the single query path are untouched (the IL scan still passes), and that a disabled translator changes nothing

## 3. CI and the monitor

- [x] 3.1 Teach `compose/ollama-stub/server.py` to answer a translation request (recognised by a marker, as the intent classifier is) with a fixed English phrase; verify `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e` passes
- [x] 3.2 Show the searched query in the monitor's Retrieval tab (original → searched when they differ) and document the diagnostics in `docs/trace-events.md`; verify a web test renders both

## 4. Measure again, verify and document

- [x] 4.1 Run `make eval-retrieval` again and compare with the **before** numbers from 1.1: Bulgarian recall@5 must rise materially and English recall@5 must not drop; stop and report if either fails. Run `make eval-selection` and `make eval-generation` as well, since retrieval feeds them
- [x] 4.2 In the running stack on :7171: ask the four Bulgarian questions from the proposal and confirm each returns the documents its English twin returns, with the monitor showing the searched query; confirm an English question shows no translation. Run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`
- [x] 4.3 Update README (a line on what is searched when the question is in another language), `DECISIONS.md` (translate vs multilingual embeddings, why in the retrieval server, the script check and its limits, measured before/after numbers) and the evals section on when to re-run; verify the sections exist

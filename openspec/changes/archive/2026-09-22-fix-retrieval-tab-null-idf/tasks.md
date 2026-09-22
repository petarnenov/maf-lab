# Tasks

## 1. Make the nullable weight visible to the type system

- [x] 1.1 In `web/src/monitor/traceData.ts`, change the `terms` element of `RetrievalData` to
      `{ term: string; idf: number | null; inVocabulary: boolean }`, and verify with
      `make build-web` that the type-check now fails at the `t.idf.toFixed(3)` call in
      `MonitorTabs.tsx` — the failure is the point of this task, and it is what proves the type
      is doing the work the design asks of it.
- [x] 1.2 Add `inVocabulary: true` to each term in the retrieval fixture in
      `web/src/monitor/fixtures.ts` and verify `make test-web` still passes, so the existing
      fixture matches the shape the server actually sends.

## 2. Render an out-of-vocabulary term

- [x] 2.1 In the query-terms table of `RetrievalTab` in `web/src/monitor/MonitorTabs.tsx`, render
      a term whose `idf` is `null` as an out-of-vocabulary term — no weight, and text saying the
      term cannot match on BM25 — and keep the weight rendering unchanged for a term that has
      one. Verify `make build-web` type-checks clean, which also confirms task 1.1's failure is
      resolved by the guard rather than by a cast.
- [x] 2.2 Style the out-of-vocabulary cell in `web/src/monitor/MonitorPanel.module.css` so it
      reads as a fact about the corpus rather than as an error, and verify by eye in the running
      app that the row lines up with the weighted rows in the same table.
- [x] 2.3 Add a fixture to `web/src/monitor/fixtures.ts` for a search whose query contains one
      in-vocabulary term and one out-of-vocabulary term (`idf: null`, `inVocabulary: false`),
      modelled on the reproduced trace for "What is JWE?".
- [x] 2.4 Add a test in `web/src/monitor/MonitorPanel.test.tsx` that opens the Retrieval tab on
      that fixture and asserts both rows render, the out-of-vocabulary row says the term cannot
      match on BM25, no weight is shown for it, and the rest of the search — candidates, timings —
      still renders. Verify with `make test-web`.
- [x] 2.5 Add a test for a search in which every term is out of vocabulary, asserting the tab
      renders rather than falling back to an empty or failed state. Verify with `make test-web`.

## 3. Contain a failing view

- [x] 3.1 Add an error boundary component under `web/src/components/` — a class component using
      `getDerivedStateFromError`, taking the failing area's name and rendering a fallback that
      names it and says it could not be shown, with nothing from the error on the page. Verify
      with a unit test that a child which throws yields the fallback and the error's message text
      is absent from the rendered output.
- [x] 3.2 Wrap the tab body in `web/src/monitor/MonitorPanel.tsx` (the `role="tabpanel"` element's
      content) in that boundary, keyed by the selected tab id, passing the selected tab's label as
      the name. Verify `make test-web` and `make build-web` both pass with the existing monitor
      tests unchanged.
- [x] 3.3 Add a test in `web/src/monitor/MonitorPanel.test.tsx` that renders the panel with a view
      forced to throw and asserts the fallback names that view while the panel header, the
      statistics chips and the tab strip are all still present. Verify with `make test-web`.
- [x] 3.4 Extend that test to select another tab and assert it renders normally, then return to
      the failed tab and assert it is attempted again rather than staying failed — the key-based
      reset from the design. Verify with `make test-web`.

## 4. Confirm against the real defect

- [x] 4.1 Run `make lint` and `make test` and verify both pass.
- [x] 4.2 With the stack running, open the reported conversation
      `c_3ed9519cbeb44f37a13281fe8493b8f5` at `http://localhost:7171`, select the turn for the
      question "What is JWE?", open the Retrieval tab, and verify the term `jwe` is listed as
      out-of-vocabulary, the rest of the search renders, and the browser console reports no
      `TypeError`.
- [x] 4.3 Verify the same conversation's other turns and at least one turn with no
      out-of-vocabulary term still render as before, so the guard did not change the weighted
      case.

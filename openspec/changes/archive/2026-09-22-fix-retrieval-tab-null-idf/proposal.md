# Proposal

## Why

Opening the monitor's Retrieval tab on a turn whose question contained a word outside the
BM25 vocabulary blanks the whole chat screen with
`Uncaught TypeError: Cannot read properties of null (reading 'toFixed')`.

Two independent defects line up to produce that:

1. Search diagnostics report an out-of-vocabulary query term with `idf: null` and
   `inVocabulary: false` — deliberately, because a term the corpus has never seen has no IDF
   weight to report. The Retrieval tab types that field as a plain `number` and calls
   `.toFixed(3)` on it unconditionally.
2. Nothing in the web app catches a render error, so a throw inside one monitor view unmounts
   the React root and takes the entire chat screen with it. The user loses the conversation,
   not just the tab.

Reproduced on conversation `c_3ed9519cbeb44f37a13281fe8493b8f5`: the question "What is JWE?"
tokenises to the term `jwe`, which is absent from the indexed corpus, so the trace for turn
`t_05027153571847e8aa6407b7f75a6828` carries `{"term":"jwe","idf":null,"inVocabulary":false}`.
Turn `t_e389c51a185a478e944f1d4187de0b26` in the same conversation carries the same shape.

This is not confined to live turns. Traces are persisted, and 15 of the 79 stored traces that
carry query terms already contain a null IDF — about a fifth of the recorded history cannot be
opened in the Retrieval tab, and that share only grows.

An out-of-vocabulary term is not an edge case to be tolerated — it is exactly what an operator
opens this tab to find out. A term the BM25 index has never seen cannot contribute to sparse
matching at all, which explains why a question retrieved poorly. Today that diagnosis crashes
instead of being shown.

## What Changes

- The Retrieval tab renders a query term with no IDF as an out-of-vocabulary term, using the
  `inVocabulary` flag the diagnostics already carry and the client currently ignores. The row
  says the term cannot match on BM25 rather than printing a number that does not exist.
- The trace types in the web app describe `idf` as nullable, so TypeScript rejects the next
  unguarded arithmetic on it instead of deferring the failure to a user's browser.
- A failure inside any one monitor view is contained to that view. The rest of the monitor, and
  above all the conversation, stays on screen and usable, with a message that names the view
  that failed and nothing internal.
- The diagnostics contract states what a term outside the vocabulary looks like, so the tab has
  something to rely on rather than a shape discovered from a crash.

No change to what the server computes or emits: the payload is already correct, and the
`inVocabulary` flag already distinguishes the two cases. This change makes the contract explicit
and teaches the client to read it.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `retrieval-tool`: "Retrieval diagnostics on request" gains the rule that a query term outside
  the BM25 vocabulary is reported as such and carries no IDF weight, rather than the current
  bare mention of "the BM25 query terms with their IDF weights".
- `web-ui`: "Monitor views" gains the rule that the retrieval view shows an out-of-vocabulary
  term as unmatched instead of a weight. A new requirement states that one failing monitor view
  never takes down the screen around it.

## Impact

- `web/src/monitor/MonitorTabs.tsx` — the query-terms table in `RetrievalTab`.
- `web/src/monitor/traceData.ts` — the `terms` element type.
- `web/src/monitor/MonitorPanel.tsx` — the tab body gets an error boundary around it.
- New error-boundary component under `web/src/components/`, plus its test.
- `web/src/monitor/MonitorPanel.test.tsx` and `web/src/monitor/fixtures.ts` — a fixture with an
  out-of-vocabulary term, and a view that throws.
- `openspec/specs/retrieval-tool/spec.md` and `openspec/specs/web-ui/spec.md` on sync.
- No backend, no API, no dependency, no package version moves.

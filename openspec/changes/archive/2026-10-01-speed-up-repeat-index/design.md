# Design

## Context

Measured on 2026-09-30 with the stack up: the pipeline itself took ~1.3 s for 624 unchanged billing documents; the rest
of a repeat `make index` was `dotnet run` start-up (~3 s per call, ~20 s when it re-checked the build) times three.
The skip check (content hash, second-truncated mtime, model versions) was correct: no document re-indexed on time alone.

## Decisions

- **Run the dll, rebuild by make's own dependency check.** `$(INDEXER_DLL)` depends on the sources of the indexer's
  project closure (Indexing, Retrieval, Domain, Hosting) and the build files, and is touched after the build so an
  up-to-date build is not re-checked. Faster than `dotnet run --no-build`, which still evaluates the project, and
  safer than never building.
- **Write only what changed.** `Bm25Model.Rebuild` reports whether the vocabulary or any statistic changed and moves
  `Version` only then; a tokenizer switch counts as a change. The bootstrapper reads the collection's payload schema
  once and creates only missing indexes.
- **Timeout by caller, not globally.** A search embeds one short query, where 100 s is already generous; only the
  indexer embeds batches that can take minutes, so it raises the default (configuration still wins).
- **Progress through `IProgress<IndexProgress>`.** The pipeline reports stage, done, total and the document being
  embedded, and knows nothing of the console; the CLI adapts it to `ConsoleProgress`. The bar is hand-written (no new
  package): a redrawn stderr line on a terminal, plain lines at most every 5 s otherwise, one final line. A failure
  names the exception type only.

## Risks / Trade-offs

- Console logs below Warning are hidden in the indexer CLI; the final line and the JSON summary carry what the
  "Indexing done" log said, and nothing parsed that line.
- Another process rewriting the dll with an older mtime makes make rebuild once more (~2 s); harmless.

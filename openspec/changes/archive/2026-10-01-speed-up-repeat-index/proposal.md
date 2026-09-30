# Proposal

## Why

A second `make index` with nothing changed should be over in moments, and it took from ten seconds to over half a
minute: each of its three indexer runs went through `dotnet run` (3–20 s of build checks), and each run wrote to Qdrant
even with nothing to write (the BM25 model and every payload index), so a full Docker disk failed an index run that had
no work. A batch of long code chunks on CPU Ollama could also outlast HttpClient's 100 s and cancel the whole run, and
the indexer ran silently for minutes, against the top-priority `progress-feedback` rule.

## What Changes

- `make index`, `index-portfolio`, `index-code`, `reindex`, `drift`, `rebuild-index` and `migrate` run the indexer from
  its build output; it is rebuilt only when a source, project or build file of it or of a project it references is
  newer than the dll.
- An index run over an unchanged corpus writes nothing: the BM25 model is saved only when its statistics change, and
  payload indexes are created only when the collection lacks them.
- Embedding requests honour `Models:EmbeddingTimeoutSeconds`; unset keeps HttpClient's 100 s for the services'
  searches, and the indexer defaults it to 900 s.
- The indexer shows a progress bar on stderr for `index` and `rebuild`: indeterminate while it reads the corpus,
  documents done of the total after, the document being embedded, and one final line with the outcome. The CLI's
  console logs warnings and up so logs do not break into the bar. `ConsoleProgress` lives in `Maf.Lab.Hosting` for the
  other CLI tools.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `document-indexing`: an unchanged corpus is indexed without writes; embedding requests of an index run have a long
  timeout; the indexer shows its progress.
- `make-workflow`: the index targets run the built indexer and rebuild it only when its sources changed.

## Impact

- Code: `Makefile`, `src/Maf.Lab.Indexing` (`Program`, `IndexingPipeline`, `IndexProgressBar`,
  `IndexingServiceCollectionExtensions`), `src/Maf.Lab.Retrieval` (`Bm25Model`, `CollectionBootstrapper`,
  `ModelOptions`, `ModelProviders`), `src/Maf.Lab.Hosting/Cli/ConsoleProgress`.
- Tests: `Bm25Tests`, `EmbeddingTimeoutTests`, `ConsoleProgressTests`, `IndexingPipelineTests`.
- No package moves; no route, target, project, model or lb location changes.

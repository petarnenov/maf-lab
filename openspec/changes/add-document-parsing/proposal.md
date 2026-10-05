# Proposal

## Why

The indexer reads text only. `SourceDocument` carries a `string Content`, and the corpus is Markdown and plain text. A
scanned PDF, a DOCX with tables, a slide deck, a chart or a screenshot cannot enter the index. A Markdown file that
holds its facts in a table or in an image loses them at chunking. An enterprise assistant's sources are mostly such
documents.

The user decided on 2026-10-05:
- a **parse** step runs before chunking, using a specialised parser, only for documents whose content plain chunking
  would lose: tables, charts and images that carry information;
- the parser is a plugin. **Docling** comes first (self-hosted, MIT). **LlamaParse** or a similar service can replace it
  fully with no change to the core;
- picture description by a vision model is off for now.

## What Changes

- **The loader carries bytes, a media type and attachments**: the files a Markdown document references. PDF, DOCX,
  PPTX, XLSX, images, Markdown and text are accepted.
- **A parse router** decides per document, and records why:
  - code decides what is certain;
  - for a Markdown file's images, code first drops what is clearly decorative (SVG icons, tiny images, names such as
    logo or icon). Then **all of that document's remaining image questions go to the decision engine in one request**
    (jev-usage: all questions over one state in ONE request). Each is a Noul, "Does this image carry information?",
    and the request is split at a size bound;
  - a Noul in the review band, or an engine that is unavailable, routes the document to parse;
  - decisions are cached by content hash, so an unchanged document is never asked again.
- **`IDocumentParser`** is the core's only view of a parser. It is AIP-151-shaped (Google's long-running operations:
  start, get, cancel), with a separate result call. A file and its attachments go in. Markdown and items with page and bounding-box provenance come
  out.
- **The parser is installation-scoped**, because choosing a subprocessor is a deployment decision. `MAF_DOCUMENT_PARSER`
  selects one installed `document-parser` provider. Switching between installed ones changes no code.
- **The `docling` plugin**, the first parser:
  - a worker of the project's own that serves the same routes as docling-serve's async API (`/v1/convert/file/async`,
    `/v1/status/poll/{id}`, `/v1/result/{id}`) plus the one call docling-serve lacks, `:cancel` (issue #447);
  - a pre-warmed child process, so models load once. Cancel kills it and respawns it;
  - pinned to the batch CPUs (`cpuset` and `OMP_NUM_THREADS`), so parsing never takes the search instance's cores
    (DECISIONS §77), with a pool of one child that serialises parses and answers `503` + `Retry-After` when busy;
  - tables, and charts as data (bar, line, pie). Pictures are classified but not described.
- **Index runs from the page too.** The admin screen's index run shows the parse step in its themed progress, and Esc
  stops it through the admin job's cancel route. The parse job's id is kept in the job row, so any replica can cancel
  it.
- **Parse results are cached** by content hash, parser and version. Chunks record the pages they came from, and the
  parsed output inherits the document's tenant and `acl`.

## Capabilities

### New Capabilities

- `document-parsing`: the router, the parser contract, parser plugins, the cache, and provenance.

### Modified Capabilities

- `document-indexing`: "Uniform chunk metadata" gains `pages` for chunks of parsed documents.

## Principles

- SOLID:
  - Single responsibility: routing, parsing and chunking are separate steps.
  - Open/closed: a new parser is a new plugin.
  - Liskov: every parser returns the same shape.
  - Interface segregation: the parser contract is only the operation's lifecycle.
  - Dependency inversion: the indexer depends on `IDocumentParser` and `IDecisionEngine`, never on Docling.
- Standards:
  - Operations: Google AIP-151 long-running operations (get, cancel).
  - Parsing: docling-serve's async route and payload shapes, plus `:cancel`; Docling (LF AI & Data).
  - Formats: CommonMark/GFM tables as the hand-off.
  - Patterns: a content-addressed cache; process isolation for cancellable work.
- Own: the docling worker, a process of our own (a child pool, kill-and-respawn, `:cancel`) that reimplements three
  docling-serve routes. docling-serve has no cancel (issue #447), and stop-anything requires one. It keeps
  docling-serve's route and payload shapes, so it can be dropped when upstream adds cancel. Rejected alternatives:
  docling-serve as is (no cancel), its RQ engine (`DOCLING_SERVE_ENG_KIND=rq` with RQ's `send_stop_job_command`), and
  its Ray engine. The engines add a queue to operate for what killing a child already gives. DECISIONS §87 (new,
  written when this change is applied).

## Progress

- Terminal: `make index` shows parsing inside its existing bar. Documents are counted done/total, the current
  document's state is "parsing", "waiting for the parser" or "chunking", and the router's tally is shown (direct,
  parsed, cached).
- Page: an index run started from the index administration screen shows the same in the screen's themed progress,
  from the admin job's row.

## Stopping

- Key: Ctrl+C or SIGTERM during `make index`; Esc on the index administration screen during a run
- Stop: the indexer stops at a document boundary and cancels the parse in flight through `IDocumentParser.CancelAsync`.
  - While waiting for a busy parser, the back-off sleep (capped at 30 s) takes the cancellation token and watches the
    job row, so Ctrl+C exits 130 and Esc cancels during the wait.
  - For `docling`, that kills the child process and respawns it. This is a safe point, because the worker persists
    nothing before a result is returned.
  - From the page, Esc calls `POST /api/admin/jobs/{id}/cancel`. The job row turns `canceled` atomically, and the
    worker on any replica watches it and cancels the parse by the id kept in the row.

  A document is written whole or not at all. The terminal exits 130.
- Recorded in: the admin job's row (with the parse job id), the parse job's own state, and the parse cache, which holds
  only finished results
- Shown: in the terminal, "Stopped — <n> of <total> documents indexed; <m> parsed, <k> from cache; run `make index`
  again to finish". On the page, "Stopping…" until the row says canceled, then the counts.

## Documentation impact

- `docs/plugins.md`: the `document-parser` provider contract.
- README: supported document types.
- `openspec/project.md`: the parse step; the docling worker in the `docling` plugin.
- DECISIONS §87 (new):
  - Docling first;
  - the worker exists because docling-serve has no cancel;
  - the rejected RQ and Ray engines;
  - the router's rules and what is sent to the decision engine (alt text, caption, file name, surrounding paragraph);
  - the Docling pin `quay.io/docling-project/docling-serve-cpu:v1.36.0` (Docling 2.133.0);
  - the CPU pinning.

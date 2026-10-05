# Tasks

Jev: task 1.2 adds one closed question per remaining image, all of a document's images in ONE request through
`IDecisionEngine`. Read docs/rules/jev-usage.md before it.

Depends on `introduce-plugins` and `introduce-provider-plugins` (`IDecisionEngine`). Applies the `acl` from
`document-acls`.

## 1. Load and route

- [ ] 1.1 `SourceDocument` carries bytes, a media type and attachments. The loaders accept PDF, DOCX, PPTX, XLSX,
      images, Markdown and text. Verify that today's corpus is loaded unchanged (same doc ids and hashes).
- [ ] 1.2 The router:
  - code rules and a decorative pre-filter;
  - one Noul per remaining image, in requests per document split at 64k tokens;
  - a review band that falls back to parse, and parse with "engine unavailable" on 401, 429 or 529;
  - decisions cached by content hash;
  - the indexer loading the provider through `AddMafPlugins()`. Verify with fixtures (text, MD with a table, MD with a
      logo, MD with a chart image, PDF, image) and a fake engine. The engine receives exactly one request for a
      document with three images, and a parsed MD with a chart image yields a Markdown table.

## 2. Parser contract and plugin

- [ ] 2.1 `IDocumentParser` (AIP-151 shape) and `MAF_DOCUMENT_PARSER` selecting the installed provider. With none
      installed, a document that needs parsing is skipped and named.
- [ ] 2.2 The `docling` plugin:
  - docling-serve's async route shapes plus `:cancel`;
  - a pre-warmed pool of one child with kill-and-respawn, `503` + `Retry-After` when busy, with the child's memory
    measured and recorded;
  - the CPU pin and its `make doctor` check;
  - the pinned image. Verify that a table, a bar chart
      (as data) and a scanned page come out with page provenance, and that cancel kills the child and the pool recovers.
- [ ] 2.3 The parse cache. Verify that a second `make index` parses nothing.

## 3. Pipeline, terminal and page

- [ ] 3.1 Parsed Markdown goes to the existing chunkers, and chunks record `pages`. Verify that
      `make eval SUITE=retrieval` is unchanged for today's corpus.
- [ ] 3.2 Terminal progress and Ctrl+C. Page progress and Esc through the admin job row, which gets a
      `ParseOperationId` column. `CancelAsync` gets a fresh token. `ExpireStaleAsync` cancels a dead replica's
      operation by its stored id, and the parse cache implements the lifecycle contract.
      Verify that a stop during a parse, from either place, cancels the operation, writes no partial document, and that
      a rerun finishes.

## 4. Documents

- [ ] 4.1 `docs/plugins.md`, the README, project.md and DECISIONS §87.

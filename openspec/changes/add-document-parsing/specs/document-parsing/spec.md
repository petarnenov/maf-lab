# Spec Delta

## ADDED Requirements

### Requirement: Documents are routed to parsing only when needed
Before chunking, the indexer SHALL decide for each document whether it is parsed or chunked directly:
- code SHALL decide what is certain from the media type and structure;
- for a document's images whose role code cannot tell, the decision engine SHALL be asked one Noul per image, in
  requests holding only that document's questions and bounded in size, with a review band that falls back to parsing.
  An unavailable engine SHALL route the document to parsing. Decisions SHALL be cached by content hash. It SHALL receive the
  image's alt text, caption, file name and surrounding paragraph, never the whole document.

Every decision SHALL be recorded with its reason and without content.

#### Scenario: Plain Markdown
- **WHEN** a Markdown file without tables or images is indexed
- **THEN** it is chunked directly, no parser and no decision engine is called, and the reason "rule: plain" is recorded

#### Scenario: A decorative image
- **WHEN** a Markdown file's only image is a logo, and the engine answers decorative with high confidence
- **THEN** it is chunked directly, and the reason records the engine's answer and confidence

#### Scenario: A PDF
- **WHEN** a PDF is indexed
- **THEN** it is parsed, and its tables and charts reach the chunks as Markdown tables

### Requirement: The parser is a replaceable provider
The core SHALL reach a parser only through `IDocumentParser`, a long-running operation (start, get, result, cancel)
taking a file and its attachments and returning Markdown and items with page provenance. Parsers SHALL be
installation-scoped `document-parser` provider plugins, selected by `MAF_DOCUMENT_PARSER`. Switching between installed
parsers SHALL need no code change.

#### Scenario: Switching to another parser
- **WHEN** `llamaparse` is installed and `MAF_DOCUMENT_PARSER` changes from `docling` to `llamaparse`
- **THEN** the next index run parses through LlamaParse, and no core file changed

### Requirement: Parsing can be stopped
A stop during indexing, by Ctrl+C in the terminal or by Esc on the index administration screen, SHALL cancel the parse
in flight through the parser's cancel and SHALL write no partial document. A page run SHALL keep the parse operation id
in its admin job row, so any replica can cancel it. A rerun SHALL continue from the documents not yet indexed.

#### Scenario: Esc on the admin screen during a parse
- **WHEN** an index run started from the admin screen is parsing a document and Esc is pressed
- **THEN** the job row turns canceled, the parse operation is cancelled by its id from whichever replica runs the job,
  and the screen shows "Stopping…" until the row says canceled

#### Scenario: Ctrl+C during a Docling parse
- **WHEN** Ctrl+C is pressed while Docling parses a document
- **THEN** the worker's child process for that document is killed and replaced, nothing of it is written, and the
  indexer exits 130

### Requirement: Parsing is cached and carries provenance
A parse result SHALL be cached by content hash, parser and parser version, and SHALL be reused for an unchanged
document. Every chunk from a parsed document SHALL record the pages it came from.

#### Scenario: Unchanged document
- **WHEN** `make index` runs twice over an unchanged PDF
- **THEN** the second run calls no parser, and the chunks still record their pages

### Requirement: Parsing does not take the search instance's CPUs
The parser's worker SHALL run on the batch CPUs only, with its thread counts set to match, so that search latency is
unaffected while documents are parsed.

#### Scenario: Parsing during search
- **WHEN** a large PDF is parsed while users search
- **THEN** the worker runs only on the batch CPUs, and `make doctor` reports the pin as set

### Requirement: The parser serialises its own work on the batch CPUs
The parser's worker SHALL run one parse at a time and SHALL answer `503` with `Retry-After` while busy. The indexer
SHALL back off and show that it is waiting. No cross-process lock SHALL be needed.

#### Scenario: Two tenants index at once
- **WHEN** tenant A's page run and tenant B's terminal run both reach a parse step
- **THEN** one parses while the other receives `503`, backs off and shows "waiting for the parser", and neither runs on
  the search instance's CPUs

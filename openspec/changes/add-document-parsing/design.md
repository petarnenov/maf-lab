# Design

## Context

- **Today's pipeline.** It runs `Load → Chunk → (contextualise) → Embed → Upsert` (`IndexingPipeline.cs:32`).
  `SourceDocument` carries text and a content hash. Embeddings go to `ollama-batch` with its `num_thread`
  (`ModelProviders.cs:105-118`), and this change does not touch that path.
- **Research of 2026-10-05.**
  - Docling 2.133.0 / docling-serve 1.36.0 (MIT):
    - TableFormer for tables;
    - chart extraction (bar, line and pie charts become data);
    - picture classification;
    - JSON with page and bbox provenance;
    - async API: start, poll, result;
    - **no cancel** (issue #447).
  - LlamaParse: SaaS with an EU region, Parse API v2 with cancel, priced per page.
- **Index runs start in two places:** `make index` and the admin screen (`AdminIndexEndpoints.cs:56` run, `:75`
  migrate, `:102` cancel).

## Decisions

### 1. The router

| Document | Decided by | Route |
|---|---|---|
| text; Markdown without tables or images | code | direct |
| Markdown with a GFM table | code | parse |
| Markdown with images | code drops the clearly decorative (SVG, under 64 px, `logo`/`icon` names); then **one** decision request with a closed question per remaining image | parse if any is informative, or any answer is below the confidence gate |
| PDF, DOCX, PPTX, XLSX, image | code | parse |

**The question.** Each image is one Noul, "Does this image carry information (a chart, table, diagram or data)?". A
Noul fits a yes/no judgment better than a two-option Choice without `other` (jev-usage 4.1, 6.9).
- `noul ≥ 0.7`: informative, so the document is parsed.
- `noul ≤ 0.3`: decorative.
- In between is the review band, where the document is parsed and the case is logged for review.

**What the engine sees.** Each question carries the image's alt text, caption, file name and surrounding paragraph,
never the whole document and never the image. This is recorded in DECISIONS §87, since it leaves the system when the
engine is external.

**Size, failure and caching.**
- **Size.** A document with many images splits its questions into requests below the engine's input bound (64k
  tokens). Each request still holds only questions over that one document.
- **Failure.** When the engine is unavailable (401, as in CI with a placeholder key; 429; 529), the router does not
  wait or guess: it routes the document to parse and records "engine unavailable".
- **Caching.** Router decisions are cached by document content hash, so an unchanged document is never asked again.
- **The indexer.** The indexer is a host console app with no `IDecisionEngine` today. It loads the installed
  decision-engine provider through the same `AddMafPlugins()` registration as the api (`introduce-plugins`), which registers provider plugins too,
  and reads the key from the environment as the api does.

Every decision is logged with its reason (rule, or engine and noul), without content.

### 2. `IDocumentParser`, AIP-151-shaped

```csharp
public interface IDocumentParser
{
    Task<ParseOperation> StartAsync(ParseInput input, CancellationToken ct);   // bytes, media type, file name, attachments
    Task<ParseOperation> GetAsync(string operationId, CancellationToken ct);   // queued | running | done | failed | cancelled
    Task<ParsedDocument> ResultAsync(string operationId, CancellationToken ct);
    Task CancelAsync(string operationId, CancellationToken ct);
}
```

- **`ParseInput.Attachments`** carries the files a Markdown document references (re-review 14). Docling's Markdown
  backend needs them to see a table in an image or a chart.
- **Polling.** The indexer polls with back-off. A firing token calls `CancelAsync` before returning.

### 3. The `docling` plugin's worker

The worker is our own process (declared `Own:` in the proposal). It serves docling-serve's async routes with their
shapes, plus `POST /v1/operations/{id}:cancel`. It keeps a
**pre-warmed pool** of child processes, so the layout and TableFormer models load once. Cancel kills the child running
that operation and starts a fresh one in its place. Killing is a safe point because the worker persists nothing
before it returns a result.

**CPUs and memory.**
- The container is pinned to the batch instance's CPUs (`cpuset`, `OMP_NUM_THREADS`, `MKL_NUM_THREADS`), so torch never
  takes the search instance's cores (DECISIONS §77). `make doctor` checks that the pin is set.
- **No cross-process lock (fourth audit, blocker 1, option b).** The worker runs a pool of exactly one child on the
  batch CPUs, so it serialises parses by itself. When busy it answers `503` with `Retry-After` (RFC 9110 §15.6.4), which is also the shape docling-serve's own
  backpressure uses,
  and the indexer backs off (exponential, capped at 30 s, honouring `Retry-After`), showing "waiting for the parser" in
  its bar. The back-off sleep takes the run's cancellation token and watches the job row, so a stop ends the wait at once. `ollama-batch` is already serialised by
  `OLLAMA_NUM_PARALLEL=1` (§77).
- Two runs may still parse and embed at once on the batch CPUs. That contention stays inside the batch set and never
  reaches the search instance's cores, which is what §77 protects. Terminal runs on the host need no Redis and no new
  exposed port.
- The pool size is 1. The memory of one child (layout, TableFormer and chart models) is measured in task
  2.2 and recorded in §87, with `pool size × child memory` as the container's limit.

Options:
- tables on;
- chart extraction on;
- picture classification on;
- picture description off;
- OCR only for pages without a text layer.

The image is `quay.io/docling-project/docling-serve-cpu:v1.36.0` (Docling 2.133.0).

### 4. Installation-scoped, replaceable

The parser is chosen per installation (re-review 5). Sending whole documents to a subprocessor is a deployment
decision, recorded with that deployment's DPA, so there is no per-tenant switch to fall through. `MAF_DOCUMENT_PARSER`
names the installed provider. A new provider (for example `llamaparse`, Parse API v2 with its own cancel) is a new
plugin folder. Before `llamaparse` is used with customer data, three things must be confirmed: the EU region, zero
retention in the DPA, and its LLM subprocessors.

### 5. Page runs and cancellation

- **The id is kept.** The admin job row keeps the parse operation id, in a new `ParseOperationId` column.
- **A stop from the page.** Esc on the admin screen calls the job's cancel route. The replica running the job sees the
  row change and calls `CancelAsync` with that id.
- **A fresh token for the cancel.** When a stop fires the run's token, `CancelAsync` is called with a fresh, short
  timeout token, never with the fired one, so the cancel itself is not cancelled.
- **A replica that died.** `ExpireStaleAsync` marks a dead replica's job failed. It also cancels the job's stored parse
  operation by id, and the worker cancels any operation whose caller has gone silent past a timeout.

### 6. Cache, provenance, permissions

- **Cache.** The key is the content hash, the parser and the parser version.
- **Provenance.** Chunks record `pages`.
- **Permissions.** The parsed output inherits the tenant and, after `document-acls`, the `acl`.
- **Lifecycle.** The parse cache implements `IContributesDataLifecycle` (delete per tenant), which fills its row in
  `data-lifecycle`'s inventory.

## Risks / Trade-offs

- [Parsing on CPU is slow, about 3 s a page] → mitigated three ways: the router, the cache, and an optional GPU image.
- [A future vision model on `ollama-batch`] → Docling's OpenAI-compatible picture-description call cannot send
  `num_thread`, so such a model would load on all CPUs (DECISIONS §77). The vision follow-up must route through our own
  adapter that adds it, or run the model elsewhere.
- [The engine decides wrongly about an image] → the confidence gate falls back to parse, and every decision is
  recorded.

## Open Questions

None.

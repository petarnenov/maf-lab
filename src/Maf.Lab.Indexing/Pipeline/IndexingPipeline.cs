using System.Diagnostics;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Chunking;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Indexing.Pipeline;

public sealed record IndexRequest
{
    /// <summary>Tenants whose documents are written. Null = every tenant present in the corpus.</summary>
    public IReadOnlySet<TenantId>? Tenants { get; init; }
    /// <summary>Re-embed even when content, timestamp and model are unchanged.</summary>
    public bool Force { get; init; }
    /// <summary>Overrides Indexing:ContextualRetrieval for this run.</summary>
    public bool? Contextual { get; init; }
}

/// <summary>Load → chunk → (contextualise) → embed dense + BM25 → replace per document. Idempotent.</summary>
public sealed class IndexingPipeline(
    CollectionBootstrapper bootstrapper,
    TenantScopedMaintenance store,
    Bm25Store bm25Store,
    IDenseEncoder dense,
    ContextualEnricherFactory enrichers,
    IOptions<IndexingOptions> options,
    IOptions<Retrieval.Configuration.ModelOptions> models,
    ILogger<IndexingPipeline> logger)
{
    private readonly IndexingOptions _options = options.Value;

    public async Task<IndexRunSummary> RunAsync(IndexRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await bootstrapper.EnsureAsync(ct);

        // Corpus statistics come from the whole corpus so IDF does not depend on which tenants a run writes.
        var corpus = CorpusLoader.Load(_options.ResolveCorpusRoot());
        var prepared = corpus.Documents.ToDictionary(d => d.DocId, d => ChunkBuilder.Build(d, _options.MaxChunkChars), StringComparer.Ordinal);

        var model = await bm25Store.LoadAsync(ct, bypassCache: true);
        model.Rebuild(prepared.Values.SelectMany(c => c).Select(c => c.SparseText));

        var tenants = request.Tenants ?? corpus.LayoutTenants;
        var contextual = request.Contextual ?? _options.ContextualRetrieval;
        var enricher = enrichers.Create(contextual);
        var modelVersion = dense.ModelVersion(_options.DenseVector);
        // Every configured dense vector is written on every chunk, so no write can drop the vector a rollback needs.
        // The bootstrapper has already refused to start if the collection lacks one of them.
        var vectorModels = models.Value.Embeddings.Keys.ToDictionary(v => v, dense.ModelVersion, StringComparer.Ordinal);

        int indexed = 0, unchanged = 0, written = 0;
        long deleted = 0;
        foreach (var tenant in tenants.OrderBy(t => t.Value, StringComparer.Ordinal))
        {
            var existing = (await store.ListDocumentsAsync(tenant, ct)).ToDictionary(d => d.DocId, StringComparer.Ordinal);
            var docs = corpus.Documents.Where(d => d.Tenant == tenant).ToList();

            foreach (var doc in docs)
            {
                if (!request.Force && existing.TryGetValue(doc.DocId, out var current)
                    && current.ContentHash == doc.ContentHash && current.UpdatedAt == doc.UpdatedAt && current.ModelVersion == modelVersion
                    && vectorModels.All(v => current.DenseModelVersions?.GetValueOrDefault(v.Key) == v.Value))
                {
                    unchanged++;
                    continue;
                }

                var writes = await EncodeAsync(prepared[doc.DocId], model, enricher, modelVersion, vectorModels, ct);
                var (w, d) = await store.ReplaceDocumentAsync(tenant, doc.DocId, writes, ct);
                written += w;
                deleted += d;
                indexed++;
            }

            // Documents removed from the corpus are removed from the index.
            foreach (var gone in existing.Keys.Except(docs.Select(d => d.DocId), StringComparer.Ordinal))
            {
                deleted += await store.DeleteDocumentAsync(tenant, gone, ct);
            }
        }

        await bm25Store.SaveAsync(model, ct);
        await enricher.FlushAsync(ct);

        logger.LogInformation(
            "Indexing done: tenants={Tenants} indexed={Indexed} unchanged={Unchanged} chunks_written={Written} chunks_deleted={Deleted} rejected={Rejected} contextual={Contextual} ms={Elapsed}",
            tenants.Count, indexed, unchanged, written, deleted, corpus.Rejected.Count, contextual, sw.ElapsedMilliseconds);

        return new IndexRunSummary(indexed, unchanged, written, (int)deleted, corpus.Rejected);
    }

    private async Task<List<ChunkWrite>> EncodeAsync(
        IReadOnlyList<PreparedChunk> chunks, Bm25Model model, IContextualEnricher enricher, string modelVersion,
        IReadOnlyDictionary<string, string> vectorModels, CancellationToken ct)
    {
        var contexts = new string?[chunks.Count];
        for (var i = 0; i < chunks.Count; i++)
        {
            contexts[i] = await enricher.ContextForAsync(chunks[i], ct);
        }

        var writes = new List<ChunkWrite>(chunks.Count);
        foreach (var batch in chunks.Select((c, i) => (Chunk: c, Context: contexts[i])).Chunk(_options.EmbeddingBatchSize))
        {
            var texts = batch.Select(b => b.Chunk.DenseText(b.Context)).ToList();
            var vectors = new Dictionary<string, IReadOnlyList<float[]>>(StringComparer.Ordinal);
            foreach (var name in vectorModels.Keys)
            {
                vectors[name] = await dense.EmbedDocumentsAsync(name, texts, ct);
            }
            for (var i = 0; i < batch.Length; i++)
            {
                var (chunk, context) = batch[i];
                var record = new ChunkRecord
                {
                    TenantId = chunk.Document.Tenant.Value,
                    DocId = chunk.Document.DocId,
                    ChunkId = chunk.ChunkId,
                    SourceType = chunk.Document.SourceType,
                    SourcePath = chunk.Document.SourcePath,
                    SectionPath = chunk.SectionPath,
                    Symbol = chunk.Symbol,
                    UpdatedAt = chunk.Document.UpdatedAt,
                    ModelVersion = modelVersion,
                    DenseModelVersions = vectorModels,
                    Text = chunk.Text,
                    Context = context,
                    ContentHash = chunk.Document.ContentHash,
                };
                writes.Add(new ChunkWrite(record, vectors.ToDictionary(v => v.Key, v => v.Value[i], StringComparer.Ordinal),
                    Bm25Encoder.EncodeDocument(model, chunk.SparseText)));
            }
        }
        return writes;
    }
}

public sealed class ContextualEnricherFactory(IServiceProvider services)
{
    public IContextualEnricher Create(bool enabled) =>
        enabled
            ? (IContextualEnricher)Microsoft.Extensions.DependencyInjection.ActivatorUtilities.GetServiceOrCreateInstance<ContextualEnricher>(services)
            : new NoContextEnricher();
}

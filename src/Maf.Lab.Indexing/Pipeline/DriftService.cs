using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Indexing.Pipeline;

/// <summary>Compares source updated_at with indexed updated_at; a document missing from the index counts as stale.</summary>
public sealed class DriftService(TenantScopedMaintenance store, IOptions<IndexingOptions> options)
{
    public async Task<DriftReport> ComputeAsync(IReadOnlySet<TenantId>? tenants, CancellationToken ct)
    {
        var corpus = CorpusLoader.Load(options.Value.ResolveCorpusRoot(), tenants);
        var scope = tenants ?? corpus.LayoutTenants;

        var indexed = new Dictionary<string, IndexedDocument>(StringComparer.Ordinal);
        foreach (var tenant in scope)
        {
            foreach (var doc in await store.ListDocumentsAsync(tenant, ct))
            {
                indexed[doc.DocId] = doc;
            }
        }

        var stale = new List<StaleDocument>();
        var missing = new List<string>();
        foreach (var source in corpus.Documents)
        {
            if (!indexed.TryGetValue(source.DocId, out var doc))
            {
                missing.Add(source.DocId);
            }
            else if (source.UpdatedAt > doc.UpdatedAt)
            {
                stale.Add(new StaleDocument(source.DocId, source.SourcePath, source.UpdatedAt, doc.UpdatedAt));
            }
        }

        var total = corpus.Documents.Count;
        var staleCount = stale.Count + missing.Count;
        var percent = total == 0 ? 0 : Math.Round(100.0 * staleCount / total, 2);
        return new DriftReport(total, staleCount, percent, stale, missing);
    }
}

using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Indexing.Pipeline;

/// <summary>
/// Compares source updated_at with indexed updated_at; a document missing from the index counts as stale. The billing
/// graph is compared with the same source documents in the same tenant scope (add-graph-drift): by the source content
/// hash its document nodes record, so the report says which store is behind and which rebuild fixes it.
/// </summary>
public sealed class DriftService(
    TenantScopedMaintenance store,
    TenantScopedGraphMaintenance graph,
    IOptions<IndexingOptions> options,
    ILogger<DriftService>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<DriftService>.Instance;

    public async Task<DriftReport> ComputeAsync(IReadOnlySet<TenantId>? tenants, CancellationToken ct, IProgress<IndexProgress>? progress = null)
    {
        progress?.Report(new IndexProgress("reading corpus", 0, null));
        var corpus = options.Value.LoadCorpus(tenants);
        var scope = tenants ?? corpus.LayoutTenants;

        var indexed = new Dictionary<string, IndexedDocument>(StringComparer.Ordinal);
        var listed = 0;
        foreach (var tenant in scope)
        {
            progress?.Report(new IndexProgress("listing the index", listed, scope.Count, tenant.Value));
            foreach (var doc in await store.ListDocumentsAsync(tenant, ct))
            {
                indexed[doc.DocId] = doc;
            }
            listed++;
            progress?.Report(new IndexProgress("listing the index", listed, scope.Count));
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

        progress?.Report(new IndexProgress("reading the graph", listed, scope.Count));
        var graphSource = options.Value.GraphSource;
        var graphDrift = string.IsNullOrWhiteSpace(graphSource)
            ? GraphDrift.Unavailable(GraphDrift.NotBuilt)
            : await GraphSectionAsync(token => graph.ListDocumentsAsync(graphSource, token), corpus.Documents, scope, _logger, ct);

        var total = corpus.Documents.Count;
        var staleCount = stale.Count + missing.Count;
        return new DriftReport(total, staleCount, Percent(staleCount, total), stale, missing, graphDrift);
    }

    /// <summary>
    /// Reads the graph's document nodes and compares them; an unreachable graph store makes the section unavailable
    /// instead of failing the report. Only the exception type is logged — never its message, the address or the query.
    /// </summary>
    internal static async Task<GraphDrift> GraphSectionAsync(Func<CancellationToken, Task<IReadOnlyList<GraphDocument>>> listDocuments,
        IReadOnlyList<SourceDocument> sources, IReadOnlySet<TenantId> scope, ILogger logger, CancellationToken ct)
    {
        IReadOnlyList<GraphDocument> nodes;
        try
        {
            nodes = await listDocuments(ct);
        }
        catch (Exception ex) when (ex is Neo4j.Driver.ServiceUnavailableException or Neo4j.Driver.SessionExpiredException
                                       or Neo4j.Driver.TransientException or Neo4j.Driver.SecurityException)
        {
            logger.LogWarning("Graph drift unavailable: {ErrorType}", ex.GetType().Name);
            return GraphDrift.Unavailable(GraphDrift.Unreachable);
        }
        return CompareGraph(sources, scope, nodes);
    }

    /// <summary>The graph against the source: missing, built from other content (or before it was recorded), and gone from the source.</summary>
    internal static GraphDrift CompareGraph(IReadOnlyList<SourceDocument> sources, IReadOnlySet<TenantId> scope, IReadOnlyList<GraphDocument> nodes)
    {
        var inScope = nodes.Where(n => scope.Contains(n.Tenant)).ToDictionary(n => n.DocId, StringComparer.Ordinal);
        var missing = new List<string>();
        var behind = new List<string>();
        foreach (var source in sources)
        {
            if (!inScope.TryGetValue(source.DocId, out var node))
            {
                missing.Add(source.DocId);
            }
            else if (node.DocHash != source.ContentHash)
            {
                behind.Add(source.DocId);
            }
        }
        var sourceIds = sources.Select(s => s.DocId).ToHashSet(StringComparer.Ordinal);
        var notInCorpus = inScope.Keys.Where(id => !sourceIds.Contains(id)).Order(StringComparer.Ordinal).ToList();
        var outOfSync = missing.Count + behind.Count;
        return new GraphDrift(true, null, outOfSync, Percent(outOfSync, sources.Count), missing, behind, notInCorpus);
    }

    private static double Percent(int part, int total) => total == 0 ? 0 : Math.Round(100.0 * part / total, 2);
}

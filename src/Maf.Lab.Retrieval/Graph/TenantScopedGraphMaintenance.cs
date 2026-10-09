using System.Diagnostics;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Hosting;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Maf.Lab.Retrieval.Graph;

public sealed record GraphWriteCounts(int Written, int Unchanged, int Rejected)
{
    public static readonly GraphWriteCounts None = new(0, 0, 0);
    public GraphWriteCounts Add(GraphWriteCounts other) => new(Written + other.Written, Unchanged + other.Unchanged, Rejected + other.Rejected);
}

public sealed record GraphRemoval(int Nodes, int Edges);

/// <summary>Counts of what the graph holds, for a build's summary.</summary>
public sealed record GraphCounts(long Nodes, long Edges);

/// <summary>
/// A billing document node as drift sees it (add-graph-drift): its tenant, document id and the source content hash it was
/// built from — null for a node built before it was recorded. Never a title, path or text.
/// </summary>
public sealed record GraphDocument(TenantId Tenant, string DocId, string? DocHash);

/// <summary>
/// THE graph write path, for the indexer only. Every write names the tenant of what it writes (from the corpus layout or
/// a seed record, never from a request); a node without one is rejected. Nodes and edges are merged on stable keys and
/// stamped with the build's run id, so a build over unchanged sources writes nothing new and a later
/// <see cref="RemoveStaleAsync"/> deletes only what the latest run no longer produced.
/// </summary>
public sealed class TenantScopedGraphMaintenance(IDriver driver, IOptions<GraphOptions> options)
{
    private const int BatchSize = 500;
    private readonly string _database = options.Value.Database;

    /// <summary>Uniqueness of (tenant, key) per label, and the lookups the read templates use.</summary>
    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        foreach (var label in GraphLabels.All)
        {
            await RunAsync($"CREATE CONSTRAINT {label.ToLowerInvariant()}_tenant_key IF NOT EXISTS FOR (n:{label}) REQUIRE (n.tenant_id, n.key) IS UNIQUE", null, ct);
        }
        foreach (var (label, property) in new[]
                 {
                     (GraphLabels.Method, "display"), (GraphLabels.Method, "name"), (GraphLabels.Method, "type_name"),
                     (GraphLabels.Method, "path"), (GraphLabels.Method, "full_name"),
                 })
        {
            await RunAsync($"CREATE INDEX {label.ToLowerInvariant()}_{property} IF NOT EXISTS FOR (n:{label}) ON (n.{property})", null, ct);
        }
        foreach (var label in GraphLabels.All)
        {
            await RunAsync($"CREATE INDEX {label.ToLowerInvariant()}_run IF NOT EXISTS FOR (n:{label}) ON (n.source, n.run_id)", null, ct);
        }
        // The retrieval subgraph (neo4j-retrieval-spike): a term is looked up by key alone, and the dense index declares the
        // properties a SEARCH may filter on inside the index — the tenant above all, so a small tenant is not shortchanged.
        await RunAsync($"CREATE INDEX term_key IF NOT EXISTS FOR (n:{GraphLabels.Term}) ON (n.key)", null, ct);
        await RunAsync($$$"""
            CREATE VECTOR INDEX {{{RetrievalGraph.DenseIndex}}} IF NOT EXISTS
            FOR (c:{{{GraphLabels.RetrievalChunk}}}) ON c.{{{RetrievalGraph.DenseProperty}}}
            WITH [c.tenant_id, c.{{{RetrievalGraph.CollectionProperty}}}, c.source_type]
            OPTIONS {indexConfig: {`vector.dimensions`: {{{RetrievalGraph.DenseDimensions}}}, `vector.similarity_function`: 'cosine'}}
            """, null, ct);
    }

    public async Task<GraphWriteCounts> WriteNodesAsync(string source, string runId, IReadOnlyList<GraphNode> nodes, CancellationToken ct)
    {
        RequireSource(source);
        var rejected = nodes.Count(n => !n.HasTenant || string.IsNullOrEmpty(n.Key));
        var counts = new GraphWriteCounts(0, 0, rejected);
        foreach (var group in nodes.Where(n => n.HasTenant && !string.IsNullOrEmpty(n.Key)).GroupBy(n => n.Label))
        {
            var label = RequireLabel(group.Key);
            foreach (var batch in group.Chunk(BatchSize))
            {
                var rows = batch.Select(n => new Dictionary<string, object?>
                {
                    ["tenant"] = n.Tenant.Value,
                    ["key"] = n.Key,
                    ["props"] = n.Properties.ToDictionary(p => p.Key, p => p.Value),
                    ["hash"] = n.ContentHash,
                }).ToList();
                var records = await RunAsync($$"""
                    UNWIND $rows AS row
                    MERGE (n:{{label}} {tenant_id: row.tenant, key: row.key})
                    WITH n, row, coalesce(n.content_hash = row.hash, false) AS same
                    SET n += row.props, n.content_hash = row.hash, n.source = $source, n.run_id = $run
                    RETURN count(n) AS total, sum(CASE WHEN same THEN 1 ELSE 0 END) AS unchanged
                    """, new() { ["rows"] = rows, ["source"] = source, ["run"] = runId }, ct);
                var total = records[0]["total"].As<int>();
                var unchanged = records[0]["unchanged"].As<int>();
                counts = counts.Add(new GraphWriteCounts(total - unchanged, unchanged, 0));
            }
        }
        return counts;
    }

    public async Task<GraphWriteCounts> WriteEdgesAsync(string source, string runId, IReadOnlyList<GraphEdge> edges, CancellationToken ct)
    {
        RequireSource(source);
        var rejected = edges.Count(e => e.FromTenant.Value is null || e.ToTenant.Value is null);
        var counts = new GraphWriteCounts(0, 0, rejected);
        foreach (var group in edges.Where(e => e.FromTenant.Value is not null && e.ToTenant.Value is not null)
                     .GroupBy(e => (e.FromLabel, e.Type, e.ToLabel)))
        {
            var from = RequireLabel(group.Key.FromLabel);
            var to = RequireLabel(group.Key.ToLabel);
            var type = RequireRelation(group.Key.Type);
            foreach (var batch in group.Chunk(BatchSize))
            {
                var rows = batch.Select(e => new Dictionary<string, object?>
                {
                    ["ft"] = e.FromTenant.Value, ["fk"] = e.FromKey, ["tt"] = e.ToTenant.Value, ["tk"] = e.ToKey,
                    ["props"] = e.Properties?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, object?>(),
                }).ToList();
                var records = await RunAsync($$"""
                    UNWIND $rows AS row
                    MATCH (a:{{from}} {tenant_id: row.ft, key: row.fk})
                    MATCH (b:{{to}} {tenant_id: row.tt, key: row.tk})
                    MERGE (a)-[r:{{type}}]->(b)
                    WITH r, row, r.run_id IS NOT NULL AS existed
                    SET r += row.props, r.source = $source, r.run_id = $run
                    RETURN count(r) AS total, sum(CASE WHEN existed THEN 1 ELSE 0 END) AS unchanged
                    """, new() { ["rows"] = rows, ["source"] = source, ["run"] = runId }, ct);
                var total = records[0]["total"].As<int>();
                var unchanged = records[0]["unchanged"].As<int>();
                // An edge whose ends were not written is not created; it counts as rejected.
                counts = counts.Add(new GraphWriteCounts(total - unchanged, unchanged, batch.Length - total));
            }
        }
        return counts;
    }

    /// <summary>Deletes the source's edges and nodes that the given run did not write.</summary>
    public async Task<GraphRemoval> RemoveStaleAsync(string source, string runId, CancellationToken ct)
    {
        RequireSource(source);
        var parameters = new Dictionary<string, object?> { ["source"] = source, ["run"] = runId };
        var edges = await RunAsync("""
            MATCH ()-[r]->() WHERE r.source = $source AND r.run_id <> $run
            DELETE r
            RETURN count(r) AS removed
            """, parameters, ct);
        var nodes = await RunAsync("""
            MATCH (n) WHERE n.source = $source AND n.run_id <> $run
            DETACH DELETE n
            RETURN count(n) AS removed
            """, parameters, ct);
        return new GraphRemoval(nodes[0]["removed"].As<int>(), edges[0]["removed"].As<int>());
    }

    public async Task<GraphCounts> CountAsync(string? source, CancellationToken ct)
    {
        var parameters = new Dictionary<string, object?> { ["source"] = source };
        var nodes = await RunAsync("MATCH (n) WHERE $source IS NULL OR n.source = $source RETURN count(n) AS c", parameters, ct);
        var edges = await RunAsync("MATCH ()-[r]->() WHERE $source IS NULL OR r.source = $source RETURN count(r) AS c", parameters, ct);
        return new GraphCounts(nodes[0]["c"].As<long>(), edges[0]["c"].As<long>());
    }

    /// <summary>
    /// Every document node of a source, for drift reporting: one fixed query, no tenant from the caller — the caller
    /// keeps the tenants it may report. A maintenance read: no tool or agent code reaches it (graph-store).
    /// </summary>
    public async Task<IReadOnlyList<GraphDocument>> ListDocumentsAsync(string source, CancellationToken ct)
    {
        RequireSource(source);
        var rows = await RunAsync($"""
            MATCH (d:{GraphLabels.Document}) WHERE d.source = $source
            RETURN d.tenant_id AS tenant, d.key AS key, d.{GraphProperties.DocHash} AS hash
            ORDER BY key
            """, new() { ["source"] = source }, ct, read: true);
        // Every write carries a valid tenant, so the filter only guards against a node written by hand.
        return [.. rows
            .Select(r => (Ok: TenantId.TryParse(r["tenant"].As<string?>(), out var tenant), Tenant: tenant, Row: r))
            .Where(x => x.Ok)
            .Select(x => new GraphDocument(x.Tenant, x.Row["key"].As<string>(), x.Row["hash"].As<string?>()))];
    }

    /// <summary>
    /// How many chunks of a Qdrant collection the retrieval spike's copy holds (neo4j-retrieval-spike): what the comparison
    /// checks against the collection's own count before it scores anything. A maintenance read; no tool reaches it.
    /// </summary>
    public async Task<long> CountRetrievalChunksAsync(string collection, CancellationToken ct)
    {
        var rows = await RunAsync($"MATCH (c:{GraphLabels.RetrievalChunk}) WHERE c.{RetrievalGraph.CollectionProperty} = $collection RETURN count(c) AS c",
            new() { ["collection"] = collection }, ct, read: true);
        return rows[0]["c"].As<long>();
    }

    /// <summary>
    /// Whether the graph store answers, as a query the caller can cancel (stop-anything): the driver's own
    /// connectivity check takes no token, so a stopped caller would otherwise only abandon it.
    /// </summary>
    public async Task VerifyConnectivityAsync(CancellationToken ct) => await RunAsync("RETURN 1 AS ok", null, ct, read: true);

    /// <summary>Ends the query whose transaction carries <paramref name="stopId"/> (stop-anything); how many it ended.</summary>
    private async Task<long> TerminateAsync(string stopId, CancellationToken ct)
    {
        var result = await driver.ExecutableQuery(GraphStop.Terminate).WithParameters(new { stopId }).ExecuteAsync(ct);
        return result.Result.Count > 0 ? result.Result[0]["terminated"].As<long>() : 0;
    }

    private async Task<IReadOnlyList<IRecord>> RunAsync(string cypher, Dictionary<string, object?>? parameters, CancellationToken ct, bool read = false)
    {
        using var span = LabTelemetry.Source.StartActivity(read ? "graph.maintenance_read" : "graph.write");
        var started = Stopwatch.GetTimestamp();
        // Stoppable on the server, not just abandoned (stop-anything).
        var result = await GraphStop.RunAsync(read ? RoutingControl.Readers : RoutingControl.Writers, _database, config =>
            driver.ExecutableQuery(cypher)
                .WithParameters(parameters?.ToDictionary(p => p.Key, p => p.Value!) ?? [])
                .WithConfig(config)
                .ExecuteAsync(ct), TerminateAsync, ct);
        span?.SetTag("graph.rows", result.Result.Count);
        span?.SetTag("graph.duration_ms", Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1));
        return result.Result;
    }

    private static string RequireLabel(string label) =>
        GraphLabels.All.Contains(label) ? label : throw new ArgumentException($"'{label}' is not a graph label.", nameof(label));

    private static string RequireRelation(string type) =>
        GraphRelations.All.Contains(type) ? type : throw new ArgumentException($"'{type}' is not a graph relationship type.", nameof(type));

    private static void RequireSource(string source)
    {
        if (!GraphSources.Writable.Contains(source))
        {
            throw new ArgumentException($"'{source}' is not a graph source.", nameof(source));
        }
    }
}

/// <summary>A short, order-independent hash of a node's properties.</summary>

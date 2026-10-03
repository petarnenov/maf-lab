using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Corpus;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>The graph half of drift (add-graph-drift): the billing graph against the source, in the caller's tenant scope.</summary>
public class GraphDriftTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TenantId A = TenantId.Firm("firm-a");
    private static readonly TenantId B = TenantId.Firm("firm-b");
    private static readonly IReadOnlySet<TenantId> ScopeA = new HashSet<TenantId> { A, TenantId.Shared };

    private static SourceDocument Doc(TenantId tenant, string path, string content = "text") =>
        new(tenant, "docs", path, "/tmp/" + path, content, DateTimeOffset.UnixEpoch);

    private static GraphDocument Node(SourceDocument doc) => new(doc.Tenant, doc.DocId, doc.ContentHash);

    [Fact]
    public void A_graph_built_from_the_same_sources_is_in_sync()
    {
        var sources = new[] { Doc(A, "docs/a.md"), Doc(TenantId.Shared, "docs/fees.md") };

        var drift = DriftService.CompareGraph(sources, ScopeA, [.. sources.Select(Node)]);

        Assert.Equal((true, 0, 0.0), (drift.Available, drift.OutOfSync, drift.OutOfSyncPercent));
        Assert.Empty(drift.MissingFromGraph);
        Assert.Empty(drift.Behind);
        Assert.Empty(drift.NotInCorpus);
    }

    [Fact]
    public void A_node_built_from_other_content_or_before_the_hash_was_recorded_is_behind()
    {
        var changed = Doc(A, "docs/changed.md", "new text");
        var old = Doc(A, "docs/old.md");
        var fresh = Doc(A, "docs/fresh.md");

        var drift = DriftService.CompareGraph([changed, old, fresh], ScopeA,
        [
            Node(Doc(A, "docs/changed.md", "old text")),
            new GraphDocument(A, old.DocId, null),
            Node(fresh),
        ]);

        Assert.Equal([changed.DocId, old.DocId], drift.Behind);
        Assert.Equal((2, 66.67), (drift.OutOfSync, drift.OutOfSyncPercent));
    }

    [Fact]
    public void A_source_document_without_a_node_is_missing_from_the_graph()
    {
        var added = Doc(A, "docs/new.md");

        var drift = DriftService.CompareGraph([added], ScopeA, []);

        Assert.Equal([added.DocId], drift.MissingFromGraph);
        Assert.Equal((1, 100.0), (drift.OutOfSync, drift.OutOfSyncPercent));
    }

    [Fact]
    public void A_node_whose_source_is_gone_is_listed_but_not_counted()
    {
        var kept = Doc(A, "docs/kept.md");

        var drift = DriftService.CompareGraph([kept], ScopeA, [Node(kept), new GraphDocument(A, "firm-a/docs/deleted.md", "abcd")]);

        Assert.Equal(["firm-a/docs/deleted.md"], drift.NotInCorpus);
        Assert.Equal((0, 0.0), (drift.OutOfSync, drift.OutOfSyncPercent));
    }

    [Fact]
    public void Another_firms_nodes_are_never_reported()
    {
        var mine = Doc(A, "docs/a.md");

        var drift = DriftService.CompareGraph([mine], ScopeA, [Node(mine), new GraphDocument(B, "firm-b/docs/secret.md", "ffff")]);

        Assert.DoesNotContain(drift.NotInCorpus.Concat(drift.Behind).Concat(drift.MissingFromGraph), id => id.StartsWith("firm-b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreachable_graph_store_makes_the_section_unavailable_without_its_message()
    {
        var drift = await DriftService.GraphSectionAsync(
            _ => throw new Neo4j.Driver.ServiceUnavailableException("bolt://neo4j:7687 refused"),
            [Doc(A, "docs/a.md")], ScopeA, NullLogger.Instance, Ct);

        Assert.Equal((false, "unreachable", 0), (drift.Available, drift.Reason, drift.OutOfSync));
        Assert.Empty(drift.MissingFromGraph);
    }

    [Fact]
    public async Task Any_other_failure_still_fails()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => DriftService.GraphSectionAsync(
            _ => throw new InvalidOperationException("bug"), [], ScopeA, NullLogger.Instance, Ct));
    }

    [Fact]
    public void The_drift_command_ends_with_both_halves_or_says_the_graph_was_unavailable()
    {
        var synced = new DriftReport(624, 0, 0, [], [], new GraphDrift(true, null, 3, 0.48, [], ["x", "y", "z"], []));
        var down = synced with { Graph = GraphDrift.Unavailable(GraphDrift.Unreachable) };

        Assert.Equal("624 documents: index 0 stale, graph 3 out of sync", Maf.Lab.Indexing.Program.DriftSummary(synced));
        Assert.Equal("624 documents: index 0 stale, graph unavailable", Maf.Lab.Indexing.Program.DriftSummary(down));
    }
}

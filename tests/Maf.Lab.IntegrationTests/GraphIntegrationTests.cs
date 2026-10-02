using System.Text.Json;
using Maf.Lab.CodeSearch.Tools;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Graph;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Role = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.IntegrationTests;

/// <summary>The graph store end to end on a real Neo4j: tenant boundary, idempotent builds, and the code graph of this repository.</summary>
[Collection(GraphCollection.Name)]
public sealed class GraphIntegrationTests(Neo4jFixture neo4j) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TenantId A = TenantId.Firm("firm-a");
    private static readonly TenantId B = TenantId.Firm("firm-b");
    private static readonly Principal FirmA = new("u-a", A, Role.ADVISOR, []);
    private static readonly Principal FirmB = new("u-b", B, Role.ADVISOR, []);

    private readonly string _work = Directory.CreateTempSubdirectory("maf-lab-graph-").FullName;

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private static GraphNode Node(string label, TenantId tenant, string key, string? name = null) =>
        new(label, tenant, key, new Dictionary<string, object?> { ["name"] = name, ["title"] = name ?? key, ["source_type"] = "docs", ["path"] = key });

    private static GraphEdge Edge(string fromLabel, TenantId fromTenant, string fromKey, string type, string toLabel, TenantId toTenant, string toKey) =>
        new(fromLabel, fromTenant, fromKey, type, toLabel, toTenant, toKey);

    [Fact]
    public async Task A_trace_never_crosses_into_another_firm_even_through_a_shared_node()
    {
        await using var services = neo4j.Services();
        await neo4j.ResetAsync(services);
        var maintenance = services.GetRequiredService<TenantScopedGraphMaintenance>();
        await maintenance.EnsureSchemaAsync(Ct);
        await maintenance.WriteNodesAsync(GraphSources.Billing, "r1",
        [
            Node(GraphLabels.Account, A, "A-1", "Ridgeline"),
            Node(GraphLabels.Household, A, "HH-1"),
            Node(GraphLabels.Document, TenantId.Shared, "shared/docs/fees.md", "Fee schedules"),
            Node(GraphLabels.FeeSchedule, B, "NW-SECRET-2026-001"),
            Node(GraphLabels.Account, B, "B-1", "Northwind"),
            Node(GraphLabels.Document, B, "firm-b/docs/b.md", "Northwind profile"),
        ], Ct);
        await maintenance.WriteEdgesAsync(GraphSources.Billing, "r1",
        [
            Edge(GraphLabels.Account, A, "A-1", GraphRelations.InHousehold, GraphLabels.Household, A, "HH-1"),
            // The bridge: a shared document mentions the firm A account and a firm B fee schedule.
            Edge(GraphLabels.Document, TenantId.Shared, "shared/docs/fees.md", GraphRelations.Mentions, GraphLabels.Account, A, "A-1"),
            Edge(GraphLabels.Document, TenantId.Shared, "shared/docs/fees.md", GraphRelations.Mentions, GraphLabels.FeeSchedule, B, "NW-SECRET-2026-001"),
            // A firm B node linked straight to the firm A household.
            Edge(GraphLabels.Account, B, "B-1", GraphRelations.InHousehold, GraphLabels.Household, A, "HH-1"),
            Edge(GraphLabels.Document, B, "firm-b/docs/b.md", GraphRelations.Mentions, GraphLabels.Account, A, "A-1"),
        ], Ct);
        var graph = services.GetRequiredService<IGraphReader>();

        var trace = await graph.ReadAsync(FirmA, new BillingNeighbourhood("A-1", 2), Ct);

        var keys = trace.Related.Select(r => r.Id).Concat(trace.Documents.Select(d => d.DocumentId)).ToList();
        Assert.Contains("HH-1", keys);
        Assert.Contains("shared/docs/fees.md", keys);
        Assert.DoesNotContain(keys, k => k is "NW-SECRET-2026-001" or "B-1" or "firm-b/docs/b.md");

        // Firm B cannot start from firm A's account: it answers as for an id that does not exist.
        Assert.Null((await graph.ReadAsync(FirmB, new BillingNeighbourhood("A-1", 2), Ct)).Start);
        Assert.Null((await graph.ReadAsync(FirmA, new BillingNeighbourhood("NO-SUCH-ID", 2), Ct)).Start);
    }

    private (GraphBuildService Service, ServiceProvider Provider) BillingBuild(string accountsJson)
    {
        var seed = Path.Combine(_work, "seed");
        var corpus = Path.Combine(_work, "data");
        Directory.CreateDirectory(seed);
        Directory.CreateDirectory(Path.Combine(corpus, "firm-b", "docs"));
        Directory.CreateDirectory(Path.Combine(corpus, "firm-a", "docs"));
        File.WriteAllText(Path.Combine(seed, "accounts.json"), accountsJson);
        File.WriteAllText(Path.Combine(seed, "households.json"), """[{"firmId":"firm-a","accountId":"A-1","householdId":"HH-1"},{"firmId":"firm-a","accountId":"A-2","householdId":"HH-1"}]""");
        File.WriteAllText(Path.Combine(seed, "runs.json"), """[{"firmId":"firm-a","runId":"4410","status":"completed","periodStart":"2026-01-01","periodEnd":"2026-01-31"}]""");
        File.WriteAllText(Path.Combine(corpus, "firm-b", "docs", "profile.md"), "# Esposito Household\n\nOn fee schedule NW-INST-2026-083.");
        File.WriteAllText(Path.Combine(corpus, "firm-b", "docs", "note.md"), "# Note NW-INST-2026-083\n\nApplies to NW-INST-2026-083.");
        File.WriteAllText(Path.Combine(corpus, "firm-a", "docs", "review.md"), "# Review\n\nAccount A-2 is reviewed.");
        var provider = neo4j.Services(v =>
        {
            v["Billing:AccountsSeedPath"] = Path.Combine(seed, "accounts.json");
            v["Portfolio:SeedPath"] = Path.Combine(seed, "households.json");
            v["Billing:SeedPath"] = Path.Combine(seed, "runs.json");
            v["Graph:CorpusRoot"] = corpus;
        });
        return (provider.GetRequiredService<GraphBuildService>(), provider);
    }

    private const string TwoAccounts = """[{"firmId":"firm-a","accountId":"A-1","name":"One"},{"firmId":"firm-a","accountId":"A-2","name":"Two"}]""";

    [Fact]
    public async Task Rebuilding_unchanged_sources_writes_nothing_and_a_removed_account_disappears()
    {
        var (first, provider) = BillingBuild(TwoAccounts);
        await using (provider)
        {
            await neo4j.ResetAsync(provider);
            var initial = Assert.Single(await first.RunAsync([GraphSources.Billing], null, Ct));
            Assert.True(initial.NodesWritten > 0);

            var again = Assert.Single(await first.RunAsync([GraphSources.Billing], null, Ct));
            Assert.Equal((0, 0, 0, 0), (again.NodesWritten, again.EdgesWritten, again.NodesRemoved, again.EdgesRemoved));
            Assert.Equal((initial.NodesTotal, initial.EdgesTotal), (again.NodesTotal, again.EdgesTotal));

            var schedule = await provider.GetRequiredService<IGraphReader>().ReadAsync(FirmB, new BillingNeighbourhood("NW-INST-2026-083", 1), Ct);
            Assert.Equal(["firm-b/docs/note.md", "firm-b/docs/profile.md"], schedule.Documents.Select(d => d.DocumentId).Order(StringComparer.Ordinal));
        }

        var (second, provider2) = BillingBuild("""[{"firmId":"firm-a","accountId":"A-1","name":"One"}]""");
        await using (provider2)
        {
            var removed = Assert.Single(await second.RunAsync([GraphSources.Billing], null, Ct));
            Assert.Equal(1, removed.NodesRemoved);
            // A-2's firm edge, its household edge and the review document's mention of it.
            Assert.Equal(3, removed.EdgesRemoved);
            var graph = provider2.GetRequiredService<IGraphReader>();
            Assert.Null((await graph.ReadAsync(FirmA, new BillingNeighbourhood("A-2", 1), Ct)).Start);
            Assert.NotNull((await graph.ReadAsync(FirmA, new BillingNeighbourhood("A-1", 1), Ct)).Start);
        }
    }

    [Fact]
    public async Task The_code_graph_of_this_repository_answers_callers_and_change_impact()
    {
        await using var provider = neo4j.Services(v => v["Graph:RepositoryRoot"] = CorpusIndexFixture.RepoRoot());
        await neo4j.ResetAsync(provider);
        var summary = Assert.Single(await provider.GetRequiredService<GraphBuildService>().RunAsync([GraphSources.Code], null, Ct));
        Assert.True(summary.NodesTotal > 1000);

        var tools = new CodeGraphTools(provider.GetRequiredService<IGraphReader>(), new FixedPrincipalAccessor(FirmA), NullLogger<CodeGraphTools>.Instance);

        var trace = Structured<CodeTrace>(await tools.TraceAsync("TenantScopedSearch.QueryAsync", CallDirection.callers, 1, Ct));
        Assert.Contains(trace.Reached, h => h.Symbol.StartsWith("DocumentSearchService.", StringComparison.Ordinal) && h.Path == "src/Maf.Lab.Retrieval/Search/DocumentSearchService.cs");

        var impact = Structured<ChangeImpact>(await tools.ImpactAsync("src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs", Ct));
        Assert.Contains(impact.Declared, d => d.Symbol == "TenantScopedSearch.QueryAsync");
        var tenancyTests = Assert.Single(impact.Tests, t => t.Path == "tests/Maf.Lab.IntegrationTests/TenancyAcceptanceTests.cs");
        Assert.Contains(tenancyTests.Tests, t => t.Symbol == "TenancyAcceptanceTests.Advisor_of_firm_a_receives_only_firm_a_and_shared_chunks");
    }

    private static T Structured<T>(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value.Deserialize<T>(ModelContextProtocol.McpJsonUtilities.DefaultOptions)!;
    }
}

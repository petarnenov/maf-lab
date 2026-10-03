using Maf.Lab.Domain.Graph;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.DependencyInjection;
using Role = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// The read path's record of a read on a real Neo4j (add-graph-trace-event): the same template name, rows and truncation
/// the caller got back, the tenants it bound, and no argument value.
/// </summary>
[Collection(GraphCollection.Name)]
public sealed class GraphTraceIntegrationTests(Neo4jFixture neo4j)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TenantId A = TenantId.Firm("firm-a");
    private static readonly Principal FirmA = new("u-a", A, Role.ADVISOR, []);

    [Fact]
    public async Task Each_read_is_recorded_with_what_it_returned()
    {
        await using var services = neo4j.Services();
        await neo4j.ResetAsync(services);
        var maintenance = services.GetRequiredService<TenantScopedGraphMaintenance>();
        await maintenance.EnsureSchemaAsync(Ct);
        await maintenance.WriteNodesAsync(GraphSources.Billing, "r1",
        [
            new(GraphLabels.Account, A, "A-1", new Dictionary<string, object?> { ["name"] = "Ridgeline" }),
            new(GraphLabels.Household, A, "HH-1", new Dictionary<string, object?> { ["name"] = "Ridgeline household" }),
            new(GraphLabels.Account, A, "A-2", new Dictionary<string, object?> { ["name"] = "Ridgeline IRA" }),
        ], Ct);
        await maintenance.WriteEdgesAsync(GraphSources.Billing, "r1",
        [
            new(GraphLabels.Account, A, "A-1", GraphRelations.InHousehold, GraphLabels.Household, A, "HH-1"),
            new(GraphLabels.Account, A, "A-2", GraphRelations.InHousehold, GraphLabels.Household, A, "HH-1"),
        ], Ct);
        var graph = services.GetRequiredService<IGraphReader>();

        BillingNeighbourhoodRows full, cut;
        GraphReadRecord[] reads;
        using (var log = GraphReadLog.Begin())
        {
            full = await graph.ReadAsync(FirmA, new BillingNeighbourhood("A-1", 2), Ct);
            cut = await graph.ReadAsync(FirmA, new BillingNeighbourhood("A-1", 2, limit: 1), Ct);
            reads = [.. log.Reads];
            Assert.Equal(["firm-a", "shared"], log.TenantScope);
            Assert.DoesNotContain("A-1", log.ToJson("mcp-1").ToJsonString());
        }

        Assert.Equal(2, reads.Length);
        Assert.All(reads, r => Assert.Equal(("billing_neighbourhood_2", GraphReadLog.Ok, (string?)null), (r.Query, r.Outcome, r.ErrorType)));
        Assert.True(reads[0].Rows >= 2);
        Assert.False(reads[0].Truncated);
        Assert.False(full.Truncated);
        Assert.Equal((1, 1, true), (reads[1].Limit, reads[1].Rows, reads[1].Truncated));
        Assert.True(cut.Truncated);
        Assert.All(reads, r => Assert.True(r.DurationMs >= 0));
    }
}

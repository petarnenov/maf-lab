using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// A graph query its caller stops is stopped in Neo4j too (stop-anything), not just abandoned while the server goes on.
/// Found here: the driver alone does not do it — a cancelled ExecuteAsync(ct) ran its query to the end (≈40 s), and
/// closing the session waited for it as long. Every graph read and write therefore runs through GraphStop, driven here
/// with a query slow enough to be caught running.
/// </summary>
[Collection(GraphCollection.Name)]
public sealed class GraphCancelTests(Neo4jFixture neo4j)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_cancelled_query_ends_at_once_and_is_gone_from_the_server()
    {
        await using var services = neo4j.Services();
        var driver = services.GetRequiredService<IDriver>();
        var marker = $"stop_{Guid.NewGuid():N}";
        using var stop = new CancellationTokenSource();

        var running = GraphStop.RunAsync(RoutingControl.Readers, "neo4j", config =>
            driver.ExecutableQuery($"// {marker}\nUNWIND range(1, 2000000000) AS x WITH x WHERE x % 7 = 0 RETURN count(x) AS c")
                .WithConfig(config).ExecuteAsync(stop.Token), (id, ct) => TerminateAsync(driver, id, ct), stop.Token);
        await WaitUntilAsync(async () => await RunningAsync(driver, marker) > 0);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"the call took {clock.Elapsed} to end");
        await WaitUntilAsync(async () => await RunningAsync(driver, marker) == 0);
    }

    [Fact]
    public async Task A_query_that_is_not_stopped_returns_as_before()
    {
        await using var services = neo4j.Services();
        var driver = services.GetRequiredService<IDriver>();

        var result = await GraphStop.RunAsync(RoutingControl.Readers, "neo4j", config =>
            driver.ExecutableQuery("RETURN 41 + 1 AS answer").WithConfig(config).ExecuteAsync(Ct), (id, ct) => TerminateAsync(driver, id, ct), Ct);

        Assert.Equal(42L, result.Result[0]["answer"].As<long>());
    }

    /// <summary>What the graph classes hand GraphStop: the fixed terminate statement, on this driver.</summary>
    private static async Task<long> TerminateAsync(IDriver driver, string stopId, CancellationToken ct)
    {
        var result = await driver.ExecutableQuery(GraphStop.Terminate).WithParameters(new { stopId }).ExecuteAsync(ct);
        return result.Result[0]["terminated"].As<long>();
    }

    /// <summary>How many transactions on the server are running the query that carries <paramref name="marker"/>.</summary>
    private static async Task<long> RunningAsync(IDriver driver, string marker)
    {
        var result = await driver.ExecutableQuery(
                "SHOW TRANSACTIONS YIELD currentQuery WHERE currentQuery CONTAINS $marker AND NOT currentQuery CONTAINS 'SHOW TRANSACTIONS' RETURN count(*) AS n")
            .WithParameters(new { marker })
            .ExecuteAsync(Ct);
        return result.Result[0]["n"].As<long>();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100 && !await condition(); i++)
        {
            await Task.Delay(100, Ct);
        }
        Assert.True(await condition());
    }
}

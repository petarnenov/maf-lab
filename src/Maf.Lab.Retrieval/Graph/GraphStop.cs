using Neo4j.Driver;

namespace Maf.Lab.Retrieval.Graph;

/// <summary>
/// Makes a graph query stoppable on the server (stop-anything). The driver takes a token but only checks it between
/// steps: a cancelled query goes on running in Neo4j until it ends by itself, and closing the session waits for it. So
/// each query carries a random id in its transaction metadata, and a cancel ends that transaction with Neo4j's own
/// <c>TERMINATE TRANSACTIONS</c>. The id is all the metadata carries — no content, no tenant.
/// This class never opens the driver itself: only the tenant-scoped graph classes do, and each hands in how it runs
/// <see cref="Terminate"/>.
/// </summary>
internal static class GraphStop
{
    /// <summary>The metadata key the id travels under.</summary>
    public const string MetadataKey = "stopId";

    /// <summary>Fixed, like every Cypher this system runs: it ends only the transactions that carry the given id.</summary>
    internal const string Terminate =
        "SHOW TRANSACTIONS YIELD transactionId, metaData WHERE metaData.stopId = $stopId " +
        "WITH collect(transactionId) AS ids TERMINATE TRANSACTIONS ids YIELD transactionId RETURN count(*) AS terminated";

    private static readonly TimeSpan TerminateWithin = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="query"/> with a <see cref="QueryConfig"/> whose transaction carries a stop id, and ends that
    /// transaction on the server when <paramref name="ct"/> fires. A query ended that way surfaces as a cancellation.
    /// </summary>
    public static async Task<T> RunAsync<T>(RoutingControl routing, string database, Func<QueryConfig, Task<T>> query,
        Func<string, CancellationToken, Task<long>> terminate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var stopId = Guid.NewGuid().ToString("N");
        var config = new QueryConfig(routing, database,
            transactionConfig: new TransactionConfig { Metadata = new Dictionary<string, object> { [MetadataKey] = stopId } });
        var ended = new CancellationTokenSource();
        await using var onStop = ct.Register(() => _ = TerminateAsync(terminate, stopId, ended.Token));
        try
        {
            return await query(config);
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is not OperationCanceledException)
        {
            throw new OperationCanceledException("The graph query was stopped.", ex, ct);
        }
        finally
        {
            await ended.CancelAsync();
            ended.Dispose();
        }
    }

    /// <summary>
    /// Ends the query's transaction. The cancel can land before the query reaches the server, so it tries again for a
    /// moment, until it has ended one or the query is over.
    /// </summary>
    private static async Task TerminateAsync(Func<string, CancellationToken, Task<long>> terminate, string stopId,
        CancellationToken queryOver)
    {
        using var within = CancellationTokenSource.CreateLinkedTokenSource(queryOver);
        within.CancelAfter(TerminateWithin);
        try
        {
            while (!within.IsCancellationRequested)
            {
                if (await terminate(stopId, within.Token) > 0)
                {
                    return;
                }
                await Task.Delay(100, within.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or Neo4jException or ObjectDisposedException)
        {
            // The query is over, the store is gone, or the driver is closing: nothing is left to end.
        }
    }
}

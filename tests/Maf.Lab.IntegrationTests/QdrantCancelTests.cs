using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// A vector store call its caller stops is cancelled on its gRPC channel and ends at once (stop-anything), and the
/// caller sees it as the cancellation it is — gRPC's own report of it is an RpcException with status Cancelled.
/// </summary>
public sealed class QdrantCancelTests(QdrantFixture qdrant)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_cancelled_call_ends_at_once_as_a_cancellation()
    {
        var collection = $"stop_{Guid.NewGuid():N}";
        await using var services = qdrant.Services(collection);
        var client = services.GetRequiredService<QdrantClient>();
        await client.CreateCollectionAsync(collection, new VectorParams { Size = 256, Distance = Distance.Cosine }, cancellationToken: Ct);
        var random = new Random(7);
        // Large enough that the write is still going when the stop comes.
        var points = Enumerable.Range(0, 60_000).Select(i => new PointStruct
        {
            Id = (ulong)i,
            Vectors = Enumerable.Range(0, 256).Select(_ => (float)random.NextDouble()).ToArray(),
        }).ToList();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.UpsertAsync(collection, points, wait: true, cancellationToken: stop.Token));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"the call took {clock.Elapsed} to end");
        // And the client is still good for the next call.
        Assert.True(await client.CollectionExistsAsync(collection, Ct));
    }
}

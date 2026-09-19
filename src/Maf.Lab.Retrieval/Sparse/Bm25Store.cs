using System.Text.Json;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Maf.Lab.Retrieval.Sparse;

/// <summary>Persists the BM25 model as a single point in the meta collection; cached with a short TTL.</summary>
public sealed class Bm25Store(QdrantClient client, IOptions<QdrantOptions> options, TimeProvider time)
{
    private static readonly PointId ModelPointId = new() { Num = 1 };
    private const string PayloadKey = "bm25_model";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly string _collection = options.Value.MetaCollection;
    private Bm25Model? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<Bm25Model> LoadAsync(CancellationToken ct, bool bypassCache = false)
    {
        if (!bypassCache && _cached is not null && time.GetUtcNow() - _cachedAt < CacheTtl)
        {
            return _cached;
        }
        var points = await client.RetrieveAsync(_collection, [ModelPointId], withPayload: true, withVectors: false, cancellationToken: ct);
        var model = points.Count > 0 && points[0].Payload.TryGetValue(PayloadKey, out var json)
            ? JsonSerializer.Deserialize<Bm25Model>(json.StringValue) ?? Bm25Model.Empty()
            : Bm25Model.Empty();
        _cached = model;
        _cachedAt = time.GetUtcNow();
        return model;
    }

    public async Task SaveAsync(Bm25Model model, CancellationToken ct)
    {
        var point = new PointStruct { Id = ModelPointId, Vectors = new[] { 0f } };
        point.Payload[PayloadKey] = JsonSerializer.Serialize(model);
        await client.UpsertAsync(_collection, [point], wait: true, cancellationToken: ct);
        _cached = model;
        _cachedAt = time.GetUtcNow();
    }
}

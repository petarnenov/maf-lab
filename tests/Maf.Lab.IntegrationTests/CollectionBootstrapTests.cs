using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client.Grpc;

namespace Maf.Lab.IntegrationTests;

public class CollectionBootstrapTests(QdrantFixture qdrant)
{
    [Fact]
    public async Task Collection_has_named_vectors_tenant_index_and_per_tenant_hnsw()
    {
        var collection = $"boot_{Guid.NewGuid():N}";
        await using var services = qdrant.Services(collection);
        await services.GetRequiredService<CollectionBootstrapper>().EnsureAsync(TestContext.Current.CancellationToken);

        var info = await qdrant.RawClient().GetCollectionInfoAsync(collection, TestContext.Current.CancellationToken);
        var vectors = info.Config.Params.VectorsConfig.ParamsMap.Map;
        Assert.Equal(768ul, vectors["dense_v3"].Size);
        Assert.True(info.Config.Params.SparseVectorsConfig.Map.ContainsKey(ChunkSchema.SparseVector));
        Assert.Equal(0ul, info.Config.HnswConfig.M);
        Assert.Equal(16ul, info.Config.HnswConfig.PayloadM);

        var schema = info.PayloadSchema;
        Assert.Equal(PayloadSchemaType.Keyword, schema[ChunkSchema.TenantId].DataType);
        Assert.True(schema[ChunkSchema.TenantId].Params.KeywordIndexParams.IsTenant);
        Assert.Equal("dense_v3", Assert.Single(vectors).Key);
        foreach (var field in new[] { ChunkSchema.SourceType, ChunkSchema.DocId, ChunkSchema.ModelVersion, ChunkSchema.ModelVersionOf("dense_v3") })
        {
            Assert.Equal(PayloadSchemaType.Keyword, schema[field].DataType);
        }
        Assert.Equal(PayloadSchemaType.Datetime, schema[ChunkSchema.UpdatedAt].DataType);

        // Idempotent.
        await services.GetRequiredService<CollectionBootstrapper>().EnsureAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_collection_missing_a_configured_vector_is_refused_and_left_intact()
    {
        var ct = TestContext.Current.CancellationToken;
        var collection = $"boot_{Guid.NewGuid():N}";
        // A collection from before the multilingual model, holding one point on the retired vectors.
        var old = new VectorParamsMap();
        old.Map["dense_v1"] = new VectorParams { Size = 768, Distance = Distance.Cosine };
        old.Map["dense_v2"] = new VectorParams { Size = 384, Distance = Distance.Cosine };
        await qdrant.RawClient().CreateCollectionAsync(collection, old, cancellationToken: ct);
        var point = new PointStruct { Id = new PointId { Uuid = Guid.NewGuid().ToString() } };
        var named = new NamedVectors();
        named.Vectors["dense_v1"] = new Vector { Dense = new DenseVector { Data = { new float[768] } } };
        point.Vectors = new Vectors { Vectors_ = named };
        await qdrant.RawClient().UpsertAsync(collection, [point], wait: true, cancellationToken: ct);
        await using var services = qdrant.Services(collection);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<CollectionBootstrapper>().EnsureAsync(ct));

        Assert.Contains("dense_v3", error.Message);
        Assert.Contains("make rebuild-index", error.Message);
        Assert.Equal(1ul, await qdrant.RawClient().CountAsync(collection, exact: true, cancellationToken: ct));
    }
}

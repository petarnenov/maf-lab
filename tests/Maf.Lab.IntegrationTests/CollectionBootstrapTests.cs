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
        Assert.Equal(768ul, vectors["dense_v1"].Size);
        Assert.Equal(384ul, vectors["dense_v2"].Size);
        Assert.True(info.Config.Params.SparseVectorsConfig.Map.ContainsKey(ChunkSchema.SparseVector));
        Assert.Equal(0ul, info.Config.HnswConfig.M);
        Assert.Equal(16ul, info.Config.HnswConfig.PayloadM);

        var schema = info.PayloadSchema;
        Assert.Equal(PayloadSchemaType.Keyword, schema[ChunkSchema.TenantId].DataType);
        Assert.True(schema[ChunkSchema.TenantId].Params.KeywordIndexParams.IsTenant);
        foreach (var field in new[] { ChunkSchema.SourceType, ChunkSchema.DocId, ChunkSchema.ModelVersion })
        {
            Assert.Equal(PayloadSchemaType.Keyword, schema[field].DataType);
        }
        Assert.Equal(PayloadSchemaType.Datetime, schema[ChunkSchema.UpdatedAt].DataType);

        // Idempotent.
        await services.GetRequiredService<CollectionBootstrapper>().EnsureAsync(TestContext.Current.CancellationToken);
    }
}

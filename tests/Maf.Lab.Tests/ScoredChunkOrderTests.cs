using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Store;
using Qdrant.Client.Grpc;

namespace Maf.Lab.Tests;

/// <summary>
/// Both chunk searches give equal scores one order (score descending, then chunk id): Qdrant returns an RRF tie in
/// arbitrary order, and the same search must list the same chunks the same way every time.
/// </summary>
public class ScoredChunkOrderTests
{
    private static ChunkRecord Chunk(string id) => new()
    {
        TenantId = "firm-a",
        DocId = "d",
        ChunkId = id,
        SourceType = "docs",
        SourcePath = "p",
        SectionPath = "s",
        UpdatedAt = DateTimeOffset.UnixEpoch,
        ModelVersion = "m",
        Text = id,
        ContentHash = "h",
    };

    private static ScoredPoint Point(string id, float score)
    {
        var point = new ScoredPoint { Score = score };
        point.Payload.Add(PayloadMapper.ToPayload(Chunk(id)));
        return point;
    }

    [Fact]
    public void The_store_page_orders_a_tie_by_chunk_id()
    {
        // As Qdrant may return them: the tie in reverse id order, the higher score last.
        var page = TenantScopedSearch.Page([Point("c", 0.5f), Point("b", 0.5f), Point("a", 0.5f), Point("z", 0.9f)]);

        Assert.Equal(["z", "a", "b", "c"], page.Select(c => c.Chunk.ChunkId));
    }

    [Fact]
    public void Graph_fusion_orders_a_tie_the_same_way()
    {
        // c is first in one branch and b in the other, each second in the other: an exact RRF tie.
        IReadOnlyList<ScoredChunk>[] branches = [[new(Chunk("c"), 0.9), new(Chunk("b"), 0.8)], [new(Chunk("b"), 5), new(Chunk("c"), 4)]];

        Assert.Equal(["b", "c"], ChunkFusion.Rrf(branches).Select(c => c.Chunk.ChunkId));
    }
}

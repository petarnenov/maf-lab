using Google.Protobuf.Collections;
using Qdrant.Client.Grpc;

namespace Maf.Lab.Retrieval.Store;

internal static class PayloadMapper
{
    public static Dictionary<string, Value> ToPayload(ChunkRecord c)
    {
        var payload = new Dictionary<string, Value>
        {
            [ChunkSchema.TenantId] = c.TenantId,
            [ChunkSchema.DocId] = c.DocId,
            [ChunkSchema.ChunkId] = c.ChunkId,
            [ChunkSchema.SourceType] = c.SourceType,
            [ChunkSchema.SourcePath] = c.SourcePath,
            [ChunkSchema.SectionPath] = c.SectionPath,
            [ChunkSchema.UpdatedAt] = c.UpdatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            [ChunkSchema.ModelVersion] = c.ModelVersion,
            [ChunkSchema.Text] = c.Text,
            [ChunkSchema.ContentHash] = c.ContentHash,
        };
        if (c.Symbol is not null)
        {
            payload[ChunkSchema.Symbol] = c.Symbol;
        }
        if (c.Context is not null)
        {
            payload[ChunkSchema.Context] = c.Context;
        }
        return payload;
    }

    public static ChunkRecord FromPayload(MapField<string, Value> p) => new()
    {
        TenantId = Str(p, ChunkSchema.TenantId),
        DocId = Str(p, ChunkSchema.DocId),
        ChunkId = Str(p, ChunkSchema.ChunkId),
        SourceType = Str(p, ChunkSchema.SourceType),
        SourcePath = Str(p, ChunkSchema.SourcePath),
        SectionPath = Str(p, ChunkSchema.SectionPath),
        Symbol = p.TryGetValue(ChunkSchema.Symbol, out var s) ? s.StringValue : null,
        UpdatedAt = DateTimeOffset.TryParse(Str(p, ChunkSchema.UpdatedAt), out var u) ? u : DateTimeOffset.MinValue,
        ModelVersion = Str(p, ChunkSchema.ModelVersion),
        Text = Str(p, ChunkSchema.Text),
        Context = p.TryGetValue(ChunkSchema.Context, out var ctx) ? ctx.StringValue : null,
        ContentHash = Str(p, ChunkSchema.ContentHash),
    };

    private static string Str(MapField<string, Value> p, string key) => p.TryGetValue(key, out var v) ? v.StringValue : "";
}

namespace Maf.Lab.Domain.BulgarianHistory;

/// <summary>The Bulgarian history domain's own Qdrant collection and BM25 vocabulary: never billing's.</summary>
public static class BulgarianHistoryCollections
{
    public const string Chunks = "maf_bulgarian_history_chunks";
    public const string Meta = "maf_bulgarian_history_meta";
}

/// <summary>Wire names of the tools the Bulgarian history MCP server exposes.</summary>
public static class BulgarianHistoryTools
{
    public const string Search = "search_bulgarian_history";
}

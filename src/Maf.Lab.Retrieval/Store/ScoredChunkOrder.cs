namespace Maf.Lab.Retrieval.Store;

/// <summary>
/// The one order of a search's results, for every <see cref="IChunkSearch"/>: score descending, then chunk id (ordinal),
/// so equal scores never come back in a different order from one call to the next — a deterministic secondary key, as a
/// search engine's document-id tiebreaker is.
/// </summary>
public static class ScoredChunkOrder
{
    public static IReadOnlyList<ScoredChunk> Ordered(IEnumerable<ScoredChunk> chunks) =>
        [.. chunks.OrderByDescending(c => c.Score).ThenBy(c => c.Chunk.ChunkId, StringComparer.Ordinal)];
}

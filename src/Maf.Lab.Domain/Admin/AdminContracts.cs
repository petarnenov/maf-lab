namespace Maf.Lab.Domain.Admin;

/// <param name="Graph">The billing graph against the same source documents (add-graph-drift); null only where no graph store is wired.</param>
public sealed record DriftReport(int TotalDocuments, int StaleDocuments, double StalePercent, IReadOnlyList<StaleDocument> Stale, IReadOnlyList<string> MissingFromIndex,
    GraphDrift? Graph = null);

/// <summary>
/// The billing graph against the source (add-graph-drift). <see cref="OutOfSync"/> counts the source documents missing
/// from the graph or built from other content; <see cref="NotInCorpus"/> lists nodes whose source is gone (the next
/// <c>make graph</c> removes them) and is not part of the percentage, whose denominator is the source.
/// </summary>
/// <param name="Reason">Why the graph could not be read: <c>unreachable</c>, never a hostname or exception text.</param>
public sealed record GraphDrift(bool Available, string? Reason, int OutOfSync, double OutOfSyncPercent,
    IReadOnlyList<string> MissingFromGraph, IReadOnlyList<string> Behind, IReadOnlyList<string> NotInCorpus)
{
    public const string Unreachable = "unreachable";

    public static GraphDrift Unavailable(string reason) => new(false, reason, 0, 0, [], [], []);
}

public sealed record StaleDocument(string DocId, string SourcePath, DateTimeOffset SourceUpdatedAt, DateTimeOffset IndexedUpdatedAt);

public sealed record ModelVersionCount(string ModelVersion, long Chunks);

public sealed record IndexStatus(IReadOnlyList<ModelVersionCount> ModelVersions, string ActiveDenseVector, AdminJob? CurrentJob);

/// <summary>State is one of <see cref="AdminJobStates"/>.</summary>
public sealed record AdminJob(string JobId, string Kind, string State, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? Summary);

public sealed record IndexRunSummary(int DocumentsIndexed, int DocumentsUnchanged, int ChunksWritten, int ChunksDeleted, IReadOnlyList<RejectedDocument> Rejected);

public sealed record RejectedDocument(string Path, string Reason);

public sealed record MigrationSummary(string TargetModelVersion, long Migrated, long AlreadyCurrent);

public static class AdminJobStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    /// <summary>Stopped by an administrator (stop-anything): ended, like succeeded and failed.</summary>
    public const string Canceled = "canceled";
}

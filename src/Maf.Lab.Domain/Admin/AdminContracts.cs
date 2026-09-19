namespace Maf.Lab.Domain.Admin;

public sealed record DriftReport(int TotalDocuments, int StaleDocuments, double StalePercent, IReadOnlyList<StaleDocument> Stale, IReadOnlyList<string> MissingFromIndex);

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
}

namespace Maf.Lab.Api.Storage;

// Coverage and test-generation runs (add-coverage-dashboard-and-test-agent). These rows describe the repository, not
// a tenant: none of them carries a firm.

/// <summary>One ingested coverage report: one toolchain, measured at one commit.</summary>
public sealed class CoverageSnapshotRow
{
    public required string Id { get; set; }
    public required string CommitSha { get; set; }
    /// <summary>The working tree had uncommitted changes when it was measured.</summary>
    public bool Dirty { get; set; }
    /// <summary>dotnet | vitest</summary>
    public required string Toolchain { get; set; }
    /// <summary>official | candidate</summary>
    public required string Kind { get; set; }
    /// <summary>The run a candidate belongs to.</summary>
    public string? RunId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>One file's coverage in one snapshot. The per-line detail is a gzipped JSON blob, not a row per line.</summary>
public sealed class CoverageFileRow
{
    public required string SnapshotId { get; set; }
    /// <summary>Repo-relative, forward slashes.</summary>
    public required string Path { get; set; }
    public int LinesTotal { get; set; }
    public int LinesCovered { get; set; }
    public int BranchesTotal { get; set; }
    public int BranchesCovered { get; set; }
    public required byte[] LinesBlob { get; set; }
}

/// <summary>A file's threshold override. A file without a row uses the configured default.</summary>
public sealed class CoverageThresholdRow
{
    public required string Path { get; set; }
    public int Pct { get; set; }
    public DateTime UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}

/// <summary>One test-generation run for one file.</summary>
public sealed class TestGenRunRow
{
    public required string Id { get; set; }
    public required string Path { get; set; }
    public required string Toolchain { get; set; }
    public required string CommitSha { get; set; }
    public int TargetPct { get; set; }
    public required string Model { get; set; }
    /// <summary>The A2A task on the test agent; null until the agent accepted it.</summary>
    public string? TaskId { get; set; }
    public required string State { get; set; }
    public string? Reason { get; set; }
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; }
    public double? LastPct { get; set; }
    public long Tokens { get; set; }
    public double CostUsd { get; set; }
    public string? Branch { get; set; }
    public string? MergeCommit { get; set; }
    /// <summary>The agent's final report (testgen.report/v1), kept for the file view; includes the diff.</summary>
    public string? ReportJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public required string CreatedBy { get; set; }
    /// <summary>The replica following the run's task, and when it last said so: a lease any replica can take over.</summary>
    public string? Follower { get; set; }
    public DateTime? FollowerHeartbeatAt { get; set; }
    /// <summary>The attempt's phase the agent last reported (generating, building, testing, measuring).</summary>
    public string? Phase { get; set; }
    /// <summary>Whether the activity record hit its cap and its oldest entries were dropped.</summary>
    public bool ActivityDropped { get; set; }
}

/// <summary>
/// One entry of what the agent did during a run (add-run-activity-view), keyed by the agent's own sequence number so a
/// replayed update is stored once. Model text streams in chunks: each chunk after the first is applied to its entry and
/// moves <see cref="LastSeq"/> on, which is also how a stream finds what changed since it last looked. Content: it is
/// shown to the browser and never logged or traced.
/// </summary>
public sealed class TestGenRunActivityRow
{
    public required string RunId { get; set; }
    public long Seq { get; set; }
    public long LastSeq { get; set; }
    public DateTime At { get; set; }
    public int Attempt { get; set; }
    public required string Type { get; set; }
    public string? Phase { get; set; }
    /// <summary>The tool call or the attempt's result, as JSON.</summary>
    public string? DataJson { get; set; }
    public string? Text { get; set; }
    public bool Truncated { get; set; }
}

/// <summary>Every update a run went through, in order: what a late SSE subscriber is replayed from.</summary>
public sealed class TestGenRunEventRow
{
    public long Id { get; set; }
    public required string RunId { get; set; }
    public int Seq { get; set; }
    public DateTime At { get; set; }
    public required string Json { get; set; }
}

/// <summary>
/// The GitHub issue opened for one confirmed suspected bug of one run. Written as <c>creating</c> before the call and
/// completed after it, so verification repeated after a restart never opens a second issue for the same test.
/// </summary>
public sealed class TestGenIssueRow
{
    public required string RunId { get; set; }
    /// <summary>"testFile::test".</summary>
    public required string TestKey { get; set; }
    /// <summary>creating | created</summary>
    public required string State { get; set; }
    public int? Number { get; set; }
    public string? Url { get; set; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>The states a run can be in. Everything not in <see cref="Final"/> is active.</summary>
public static class TestGenRunState
{
    public const string Submitted = "submitted";
    public const string Working = "working";
    public const string Verifying = "verifying";
    public const string Candidate = "candidate";
    public const string Accepted = "accepted";
    public const string Discarded = "discarded";
    public const string CompletedNoChange = "completed_no_change";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string VerificationFailed = "verification_failed";

    public static readonly IReadOnlySet<string> Active = new HashSet<string> { Submitted, Working, Verifying, Candidate };

    /// <summary>The same states as an array: what a database query can translate.</summary>
    public static readonly string[] ActiveStates = [Submitted, Working, Verifying, Candidate];
    public static readonly IReadOnlySet<string> Final =
        new HashSet<string> { Accepted, Discarded, CompletedNoChange, Failed, Canceled, VerificationFailed };

    /// <summary>The active states as an SQL list, for the one-active-run-per-file index.</summary>
    internal const string ActiveSql = "'submitted', 'working', 'verifying', 'candidate'";
}

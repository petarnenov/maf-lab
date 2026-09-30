namespace Maf.Lab.Api.Coverage;

/// <summary>The Coverage screen's settings: where the repository is and what a file must reach by default.</summary>
public sealed class CoverageOptions
{
    public const string Section = "Coverage";

    /// <summary>A file's line-coverage threshold when it has no override of its own.</summary>
    public int DefaultThresholdPct { get; set; } = 80;

    /// <summary>
    /// The repository's working tree. In compose it is the host path, mounted at the same path (its worktrees record
    /// host paths). Empty: the git top level above the content root, which is right for `make dev`.
    /// </summary>
    public string RepoRoot { get; set; } = "";

    /// <summary>The branch coverage is refreshed at and accepted runs are merged into.</summary>
    public string MainBranch { get; set; } = "main";

    /// <summary>Largest uploaded report.</summary>
    public long MaxReportBytes { get; set; } = 32L * 1024 * 1024;
}

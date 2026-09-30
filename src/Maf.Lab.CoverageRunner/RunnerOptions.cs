namespace Maf.Lab.CoverageRunner;

public sealed class RunnerOptions
{
    public const string Section = "Runner";

    /// <summary>The repository, mounted read-only. Empty: the git top level above the content root (local runs).</summary>
    public string RepoRoot { get; set; } = "";

    /// <summary>Where each job's workspace and output go. Empty: a folder under the system temp directory.</summary>
    public string WorkRoot { get; set; } = "";

    /// <summary>Jobs run at once; the rest wait in order.</summary>
    public int MaxConcurrent { get; set; } = 1;

    /// <summary>A job that takes longer is stopped and reported as timed out.</summary>
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a finished job's result stays readable.</summary>
    public TimeSpan KeepResultsFor { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The .NET unit test project and its coverage settings, relative to the repository root.</summary>
    public string DotnetTestProject { get; set; } = "tests/Maf.Lab.Tests";
    public string DotnetCoverageSettings { get; set; } = "tests/Maf.Lab.Tests/coverage.config.xml";

    /// <summary>
    /// The web app's installed dependencies, prepared when the image was built (the runner has no network). Empty:
    /// the workspace must already have them, which is only true when running outside the container.
    /// </summary>
    public string WebNodeModules { get; set; } = "";

    /// <summary>Largest diff accepted.</summary>
    public int MaxDiffBytes { get; set; } = 1024 * 1024;
}

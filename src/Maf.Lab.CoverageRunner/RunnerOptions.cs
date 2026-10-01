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

    /// <summary>
    /// How long a complete result answers an identical request instead of a new run (the agent's whole-suite
    /// confirmation, then the api's verification of the same diff). Zero: never reuse.
    /// </summary>
    public TimeSpan ReuseResultsFor { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The most results kept for reuse; the oldest goes first.</summary>
    public int ReuseMaxResults { get; set; } = 16;

    /// <summary>The .NET unit test project and its coverage settings, relative to the repository root.</summary>
    public string DotnetTestProject { get; set; } = "tests/Maf.Lab.Tests";
    public string DotnetCoverageSettings { get; set; } = "tests/Maf.Lab.Tests/coverage.config.xml";

    /// <summary>
    /// The web tests' setup file, relative to the repository root. Every test loads it and none imports it, so a diff
    /// that changes it runs the whole suite even when related tests were asked for.
    /// </summary>
    public string VitestSetupFile { get; set; } = "web/src/test/setup.ts";

    /// <summary>
    /// The web app's installed dependencies, prepared when the image was built (the runner has no network). Empty:
    /// the workspace must already have them, which is only true when running outside the container.
    /// </summary>
    public string WebNodeModules { get; set; } = "";

    /// <summary>Largest diff accepted.</summary>
    public int MaxDiffBytes { get; set; } = 1024 * 1024;
}

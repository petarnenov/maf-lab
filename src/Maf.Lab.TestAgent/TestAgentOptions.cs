namespace Maf.Lab.TestAgent;

/// <summary>Where the agent reads the repository from, where it works, and how much an attempt may do.</summary>
public sealed class TestAgentOptions
{
    public const string Section = "TestAgent";

    /// <summary>The repository, mounted read-only. Empty: the git top level above the content root (local runs).</summary>
    public string RepoRoot { get; set; } = "";

    /// <summary>Where each task's scratch checkout goes. Empty: a folder under the system temp directory.</summary>
    public string WorkRoot { get; set; } = "";

    /// <summary>The coverage runner, on the internal network, and the partner id it knows this agent by.</summary>
    public string RunnerBaseUrl { get; set; } = "http://localhost:5095";
    public string RunnerPartnerId { get; set; } = "maf-lab-test-agent";
    public TimeSpan RunnerPollEvery { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How many model round-trips (each possibly calling tools) one attempt may take.</summary>
    public int MaxToolRoundsPerAttempt { get; set; } = 12;

    /// <summary>How often the model may run the tests itself within one attempt; the attempt's own run is extra.</summary>
    public int MaxTestRunsPerAttempt { get; set; } = 2;
}

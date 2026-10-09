namespace Maf.Lab.TestAgent;

/// <summary>Where the agent reads the repository from and where it works.</summary>
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

    /// <summary>How long a task's lease lasts unrenewed; a replica renews it every third of this while it runs the task.</summary>
    public TimeSpan LeaseFor { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the agent looks for tasks a stopped process left running, after the first look at start.</summary>
    public TimeSpan RecoverEvery { get; set; } = TimeSpan.FromSeconds(15);

    // How much an attempt may do (tool rounds, test runs) comes with each task, bounded by RunLimits (DECISIONS.md §61).
}

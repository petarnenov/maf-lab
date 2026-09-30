namespace Maf.Lab.Api.Coverage;

/// <summary>One model the test agent may use, as configured. Nothing about it comes from the browser.</summary>
public sealed class AgentModelOption
{
    /// <summary>The provider's model tag, e.g. <c>glm-5.3:cloud</c>.</summary>
    public string Tag { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double InputPerMTok { get; set; }
    public double OutputPerMTok { get; set; }
    public string BestFor { get; set; } = "";
    public bool Default { get; set; }
    /// <summary>The prices are the lab's estimate, not the provider's list price; the picker says so.</summary>
    public bool PriceIsEstimate { get; set; } = true;
}

/// <summary>A run's hard caps. The agent stops before an attempt that would cross either.</summary>
public sealed class RunBudget
{
    public long MaxTokens { get; set; } = 400_000;
    public double MaxCostUsd { get; set; } = 2.0;
}

/// <summary>Where the test-generation agent is, who the api is to it, and what a run may use.</summary>
public sealed class TestAgentOptions
{
    public const string Section = "TestAgent";

    /// <summary>The agent's base address on the internal network. Everything else comes from its card.</summary>
    public string BaseUrl { get; set; } = "";
    public string ClientId { get; set; } = "maf-lab-assistant";
    public string ClientSecret { get; set; } = "";

    /// <summary>How long a whole run may take before the api cancels it: long enough for five attempts.</summary>
    public TimeSpan RunDeadline { get; set; } = TimeSpan.FromMinutes(45);
    public TimeSpan CardCacheFor { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 5;
    public RunBudget Budget { get; set; } = new();

    /// <summary>
    /// The allowlist. Configuration only (appsettings), with no default in code: binding appends to a non-empty
    /// default list, which would quietly widen it.
    /// </summary>
    public List<AgentModelOption> Models { get; set; } = [];

    /// <summary>How long a model's availability answer is reused.</summary>
    public TimeSpan AvailabilityCacheFor { get; set; } = TimeSpan.FromMinutes(10);
}

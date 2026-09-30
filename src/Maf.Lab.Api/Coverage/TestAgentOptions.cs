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

/// <summary>Where the test-generation agent is, who the api is to it, and how many attempts a run has.</summary>
public sealed class TestAgentOptions
{
    public const string Section = "TestAgent";

    /// <summary>The agent's base address on the internal network. Everything else comes from its card.</summary>
    public string BaseUrl { get; set; } = "";
    public string ClientId { get; set; } = "maf-lab-assistant";
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// How long a whole run may take before the api cancels it: long enough for the attempt cap. Runs took 3–4 min
    /// per attempt at 12 tool rounds; attempts may now use up to 40.
    /// </summary>
    public TimeSpan RunDeadline { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan CardCacheFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many attempts a run asks for when the administrator chooses none: the picker's default. It defaults to, and
    /// may not exceed, the contract's <see cref="Maf.Lab.TestGen.TestGenRequest.AttemptLimit"/>; set it only to run fewer.
    /// </summary>
    public int MaxAttempts { get; set; } = Maf.Lab.TestGen.TestGenRequest.AttemptLimit;

    /// <summary>
    /// The allowlist. Configuration only (appsettings), with no default in code: binding appends to a non-empty
    /// default list, which would quietly widen it.
    /// </summary>
    public List<AgentModelOption> Models { get; set; } = [];

    /// <summary>How often each replica looks for runs nobody is following, and how often a follower renews its lease.</summary>
    public TimeSpan FollowerPollEvery { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>A lease not renewed for this long is taken over: its replica has stopped.</summary>
    public TimeSpan FollowerStaleAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait before trying to subscribe again after a stream could not be had.</summary>
    public TimeSpan ResubscribeAfter { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How often a browser's event stream looks for the run's next change.</summary>
    public TimeSpan EventPollEvery { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a model's availability answer is reused.</summary>
    public TimeSpan AvailabilityCacheFor { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the agents page waits for the agent's card before calling it unreachable.</summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long the agents page reuses one card check, so opening it repeatedly never becomes a probe storm.</summary>
    public TimeSpan ProbeCacheFor { get; set; } = TimeSpan.FromSeconds(10);
}

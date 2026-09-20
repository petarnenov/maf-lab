namespace Maf.Lab.Api.A2A;

/// <summary>Where the compliance reviewer is, and who this system is to it.</summary>
public sealed class ComplianceOptions
{
    public const string Section = "Compliance";

    /// <summary>The reviewer's base address. Everything else about it comes from its card.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>This system's own client credentials at the reviewer. Not a user's — a user is not a system.</summary>
    public string ClientId { get; set; } = "maf-lab-assistant";
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// How long a consultation waits before it reports a timeout. Shorter than a person's patience, longer than a
    /// healthy review: the reviewer takes tens of seconds by design.
    /// </summary>
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>How long a fetched card is reused before it is fetched again.</summary>
    public TimeSpan CardCacheFor { get; set; } = TimeSpan.FromMinutes(5);
}

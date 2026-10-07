namespace Maf.Lab.Api.BuiltIn;

/// <summary>
/// What is left of the built-in domains (introduce-plugins decision 5g): every domain is a plugin since
/// extract-portfolio. The two ids remain as names for the consumers a follow-up moves (feedback-review, insights,
/// topology, the eval), each allow-listed; and the capability a domain's tool requires that is not a plugin yet.
/// </summary>
public static class BuiltInDomains
{
    /// <summary>The ids the remaining consumers name; the domains themselves are the billing and portfolio plugins'.</summary>
    public const string Billing = "billing";
    public const string Portfolio = "portfolio";

    /// <summary>
    /// The capabilities a domain's tool requires that are not plugins yet, each with the setting that wires it today: in
    /// use when that setting is set. Transitional — the compliance follow-up (introduce-plugins 8.1) removes its entry,
    /// after which the plugin catalogue is the only source.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LegacyCapabilities { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["compliance"] = "Compliance:BaseUrl",
    };
}

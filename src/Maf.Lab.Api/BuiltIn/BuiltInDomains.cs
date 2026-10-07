namespace Maf.Lab.Api.BuiltIn;

/// <summary>
/// What is left of the built-in domains (introduce-plugins decision 5g): every domain is a plugin since
/// extract-portfolio. The two ids remain as names for the consumers a follow-up moves (feedback-review, insights,
/// topology, the eval), each allow-listed. A tool's required capability is a plugin in use, read from the catalogue
/// (extract-compliance).
/// </summary>
public static class BuiltInDomains
{
    /// <summary>The ids the remaining consumers name; the domains themselves are the billing and portfolio plugins'.</summary>
    public const string Billing = "billing";
    public const string Portfolio = "portfolio";
}

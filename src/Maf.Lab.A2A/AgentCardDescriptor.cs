using A2A;

namespace Maf.Lab.A2A;

/// <summary>
/// What makes one agent's card different from another's: who it says it is, and what it offers. Everything else
/// about a card — the transports, the security scheme, the signature — is the same for every agent in this lab and
/// lives in <see cref="AgentCardFactory"/>.
/// </summary>
/// <param name="PublicSkills">Offered to anyone who reads the card.</param>
/// <param name="PrivateSkills">Added only to the extended card, after the caller has authenticated.</param>
/// <param name="Scopes">Scope id → what holding it lets a caller do, as the card advertises it.</param>
public sealed record AgentCardDescriptor(
    string Name,
    string Description,
    IReadOnlyList<AgentSkill> PublicSkills,
    IReadOnlyList<AgentSkill> PrivateSkills,
    IReadOnlyDictionary<string, string> Scopes)
{
    /// <summary>Where the card points a reader who wants prose rather than JSON, relative to the public base URL.</summary>
    public string DocumentationPath { get; init; } = "/docs/http-api.md";
}

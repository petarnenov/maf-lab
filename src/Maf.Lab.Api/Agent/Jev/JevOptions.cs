namespace Maf.Lab.Api.Agent.Jev;

/// <summary>
/// Where and how the TypeSafe Jev model classifies intent. The API key is deliberately not here: it is read from the
/// <c>JEV_MAF_LAB</c> environment variable by <see cref="JevCredential"/> and nothing else, so no options dump, binder
/// or validator can ever surface it.
/// </summary>
public sealed class JevOptions
{
    public const string Section = "Jev";

    /// <summary>Base address of the System One API; CI points it at the stub.</summary>
    public string Endpoint { get; set; } = "https://api.typesafe.ai";

    /// <summary>
    /// Pinned, not <c>jev-latest</c>: the confidence floor is tuned against this version, and an alias moves when a
    /// release ships. The version that actually answered is recorded on every classification.
    /// </summary>
    public string Model { get; set; } = "jev-1.13.0";

    /// <summary>Below this confidence the choice is not acted on and the turn forces nothing. TypeSafe's starting floor.</summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>Budget for one classification; 0 disables classification and every turn forces nothing.</summary>
    public double TimeoutSeconds { get; set; } = 2;
}

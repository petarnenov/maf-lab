namespace Maf.Lab.Retrieval.Jev;

/// <summary>
/// Where TypeSafe's Jev is reached, and how the intent classifier uses it (the relevance judge's own knobs are
/// retrieval options). The API key is deliberately not here: it is read from the
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

    /// <summary>
    /// A procedural or mixed intent is acted on only when Jev's probability that the question is about the documented
    /// domain reaches this. Measured on 101 labelled questions: off-domain ≤ 0.07, in-domain ≥ 0.37 bar one
    /// transliteration; 0.2 sits in that gap, nearer the side whose error is cheaper. 0 disables the gate.
    /// </summary>
    public double MinInDomain { get; set; } = 0.2;

    /// <summary>
    /// A domain whose probability reaches this is in scope for the turn: its documentation is searched when the intent
    /// forces retrieval, and a question with two domains in scope crosses the boundary (add-portfolio-domain). When the
    /// gate above passes and no domain reaches this, the most probable domain alone is in scope, so a billing question
    /// at 0.37 behaves as it always did.
    /// </summary>
    public double MinDomainScope { get; set; } = 0.5;

    /// <summary>Budget for one classification; 0 disables classification and every turn forces nothing.</summary>
    public double TimeoutSeconds { get; set; } = 2;

    /// <summary>
    /// Asks, in the same request, which read tool a data question needs, and issues that call without the model's
    /// first call when the answer is unambiguous. Off restores exactly the two-question request. On by decision
    /// (add-jev-tool-routing): 42 of 42 data turns routed to the expected tool over six selection runs, one model call
    /// instead of two, median data turn ~0.8 s faster; intent metrics unchanged.
    /// </summary>
    public bool RouteDataTools { get; set; } = true;

    /// <summary>
    /// The routed read tool's probability must reach this. Planning probe: the expected tool scored 0.73–0.93 on data
    /// questions, the other read tool up to 0.83 on run-id questions — the run id, not the probability, separates those.
    /// </summary>
    public double MinRouteProbability { get; set; } = 0.8;
}

namespace Maf.Lab.Api.Agent;

/// <summary>
/// How the core acts on the decision engine's answers to a turn's intent request: the confidence and domain floors,
/// the routing switches and floors, and the budget. Bound from the <c>Jev</c> section, the name these keys have always had,
/// so a deployment's settings keep working (a named debt: renaming the section is a later change of its own). The
/// engine's own transport settings (endpoint, model, retries, breaker) are its provider plugin's.
/// </summary>
public sealed class IntentOptions
{
    public const string Section = "Jev";

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
    /// <remarks>0.5 by measurement (close-portfolio-domain-gaps, 64 questions, final domain descriptions): accuracy 0.953,
    /// crossing recall 0.9 and precision 0.947, against 0.922 / 0.8 / 0.941 at 0.6 — see DECISIONS.</remarks>
    public double MinDomainScope { get; set; } = 0.5;

    /// <summary>
    /// A question Jev puts in no domain — every domain's probability below <see cref="MinInDomain"/> — and does not read
    /// as small talk is marked outside the domains, and the first question of a conversation so marked is answered with a
    /// fixed reply instead of reaching the model (refuse-off-domain-questions). The floor is the gate's own: off-domain
    /// questions measured ≤ 0.07, in-domain ≥ 0.37 bar one Latin-script transliteration (0.09), which is refused and
    /// asked to rephrase. Off restores the model's own judgment on every question.
    /// </summary>
    public bool RefuseOutsideDomains { get; set; } = true;

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

    /// <summary>
    /// Asks, in the same request, what a codebase question needs — its callers, its callees, a file's impact or the code's
    /// text — and starts a structural question with the code graph call instead of a forced search_codebase, when the
    /// symbol or file can be taken from the question (route-structural-code-questions). Off restores the forced search.
    /// </summary>
    public bool RouteCodeTools { get; set; } = true;

    /// <summary>
    /// Jev's confidence in a structural answer must reach this before a graph call is routed. A wrong route costs one
    /// read-only graph call, after which the model can still search. Tuned on the code-route design split: 0.5 and 0.55
    /// both route 0.947 of the structural questions and no text question, 0.6 routes 0.895; 0.55 keeps the margin.
    /// </summary>
    public double MinCodeRouteConfidence { get; set; } = 0.55;
}

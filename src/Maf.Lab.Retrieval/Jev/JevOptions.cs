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

    /// <summary>
    /// One fixed-text request at start-up, in the background, so the first turn finds an open connection
    /// (jev-client-reuse). Never delays or fails start-up; skipped without a key.
    /// </summary>
    public bool WarmUp { get; set; } = true;

    /// <summary>Budget for the warm-up: longer than a turn's, since a cold TLS and model hop is slower and nobody waits on it.</summary>
    public double WarmUpTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Retries after a transient failure (no response, 408, 429, 5xx), always inside the caller's budget; 0 disables.
    /// One by default: with a 2 s classification budget one quick retry fits, a second rarely would.
    /// </summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Delay before the first retry, doubled for each further one (±20 % jitter); a Retry-After header wins.</summary>
    public int RetryDelayMs { get; set; } = 100;

    /// <summary>
    /// How long a pooled, kept-alive connection to Jev is reused before it is replaced — the client itself lives as long
    /// as the process, so this is what picks up a change of the endpoint's address.
    /// </summary>
    public double PooledConnectionLifetimeMinutes { get; set; } = 10;

    /// <summary>The circuit breaker in front of every Jev request in the process (add-jev-circuit-breaker).</summary>
    public JevBreakerOptions Breaker { get; set; } = new();
}

/// <summary>
/// When the process stops asking Jev for a while. Bound from <c>Jev:Breaker</c>; a threshold of 0 disables the breaker
/// and every request is sent as before.
/// </summary>
public sealed class JevBreakerOptions
{
    /// <summary>
    /// Consecutive transient failures (timeout, transport error, final 408/429/5xx) that open the circuit. Three is less
    /// than one degraded turn makes (intent, a search, a screening, the answer check), so the next turn never waits; the
    /// eval traces show no isolated timeouts, so three in a row is an outage, not noise.
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>How long an open circuit skips Jev before one real call goes through as the probe.</summary>
    public double OpenSeconds { get; set; } = 30;
}

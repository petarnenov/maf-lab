namespace Maf.Lab.Plugins.Jev;

/// <summary>
/// Where TypeSafe's Jev is reached and how the transport behaves: the endpoint, the pinned model, the warm-up, retries,
/// the connection pool and the circuit breaker. The thresholds each caller applies to an answer are the core's
/// (<c>IntentOptions</c>, bound from the same <c>Jev</c> section). The API key is deliberately not here: it is read from
/// the <c>JEV_MAF_LAB</c> environment variable by <see cref="JevCredential"/> and nothing else, so no options dump,
/// binder or validator can ever surface it.
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

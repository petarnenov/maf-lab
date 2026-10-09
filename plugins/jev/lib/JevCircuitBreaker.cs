using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Jev;

/// <summary>What one Jev call came to, as far as the breaker is concerned.</summary>
public enum JevCallResult
{
    /// <summary>Jev answered: resets the count, closes a half-open circuit.</summary>
    Success,
    /// <summary>A timeout, a transport error or a final 408/429/5xx: counts towards opening.</summary>
    Failure,
    /// <summary>No key, the caller's own cancellation, any other rejection: says nothing about Jev's availability.</summary>
    Neutral,
}

/// <summary>Permission for one call: whether it may be sent, and whether it is the half-open probe.</summary>
public readonly record struct JevCircuitTicket(bool Allowed, bool Probe);

/// <summary>
/// One per process, in front of every Jev request (add-jev-circuit-breaker). Closed, it counts consecutive transient
/// failures; at the threshold it opens and every call is skipped for the open period, sending nothing. After the period
/// the next call is sent as the probe while the others keep skipping: its success closes the circuit, its failure opens
/// it again. Only state changes are logged — never a skipped call, and never anything from a request or response.
/// </summary>
public sealed class JevCircuitBreaker(IOptions<JevOptions> options, TimeProvider time, ILogger<JevCircuitBreaker> logger)
{
    private enum State { Closed, Open, HalfOpen }

    private readonly Lock _gate = new();
    private State _state = State.Closed;
    private int _failures;
    private DateTimeOffset _openedAt;
    // When the circuit first opened in the current outage, so the closing line says how long Jev was skipped in total.
    private DateTimeOffset _firstOpenedAt;
    private bool _probing;
    private int _skipped;

    private JevBreakerOptions Settings => options.Value.Breaker;

    private bool Disabled => Settings.FailureThreshold <= 0;

    /// <summary>Asks to send one call. A ticket that is not allowed means: skip Jev, the circuit is open.</summary>
    public JevCircuitTicket Enter()
    {
        if (Disabled)
        {
            return new JevCircuitTicket(true, false);
        }
        lock (_gate)
        {
            switch (_state)
            {
                case State.Closed:
                    return new JevCircuitTicket(true, false);
                case State.Open when time.GetUtcNow() - _openedAt >= TimeSpan.FromSeconds(Settings.OpenSeconds):
                    _state = State.HalfOpen;
                    _probing = true;
                    logger.LogInformation("Jev circuit half-open: probing");
                    return new JevCircuitTicket(true, true);
                case State.HalfOpen when !_probing:
                    // The previous probe said nothing (neutral): this call probes instead.
                    _probing = true;
                    return new JevCircuitTicket(true, true);
                default:
                    _skipped++;
                    return new JevCircuitTicket(false, false);
            }
        }
    }

    /// <summary>Reports how an allowed call ended. <paramref name="reason"/> is our own failure string, never content.</summary>
    public void Exit(JevCircuitTicket ticket, JevCallResult result, string? reason)
    {
        if (Disabled || !ticket.Allowed)
        {
            return;
        }
        lock (_gate)
        {
            if (ticket.Probe)
            {
                _probing = false;
                switch (result)
                {
                    case JevCallResult.Success:
                        Close();
                        break;
                    case JevCallResult.Failure:
                        Open(reason);
                        break;
                }
                return;
            }
            // A call that started while closed and ends after the circuit opened changes nothing: the probe decides.
            if (_state != State.Closed)
            {
                return;
            }
            switch (result)
            {
                case JevCallResult.Success:
                    _failures = 0;
                    break;
                case JevCallResult.Failure when ++_failures >= Settings.FailureThreshold:
                    Open(reason);
                    break;
            }
        }
    }

    private void Open(string? reason)
    {
        if (_state == State.Closed)
        {
            _skipped = 0;
            logger.LogWarning("Jev circuit opened after {Failures} consecutive failures (last: {Reason}); skipping Jev for {OpenSeconds}s",
                _failures, reason, Settings.OpenSeconds);
            _firstOpenedAt = time.GetUtcNow();
        }
        else
        {
            logger.LogWarning("Jev circuit re-opened: probe failed ({Reason}); skipping Jev for {OpenSeconds}s", reason, Settings.OpenSeconds);
        }
        _state = State.Open;
        _openedAt = time.GetUtcNow();
    }

    private void Close()
    {
        logger.LogInformation("Jev circuit closed after {OpenMs:F0} ms; {Skipped} calls skipped",
            (time.GetUtcNow() - _firstOpenedAt).TotalMilliseconds, _skipped);
        _state = State.Closed;
        _failures = 0;
        _skipped = 0;
    }
}

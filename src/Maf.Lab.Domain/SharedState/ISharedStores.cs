namespace Maf.Lab.Domain.SharedState;

/// <summary>
/// Where a run stands while it runs, and for a while after. One interface per kind of state, so that another
/// store is another implementation rather than an edit to every caller.
/// </summary>
public interface IRunStateStore
{
    Task SaveAsync(RunState state, CancellationToken ct);

    /// <summary>The run, or null when it was never kept or is no longer kept. Ownership is the caller's to check.</summary>
    Task<RunState?> GetAsync(string runId, CancellationToken ct);
}

/// <summary>
/// A run's turn trace while it is being written, readable by every replica (agui-protocol-only): the trace no longer
/// travels on the run's stream, so the monitor reads it from here while the run is live, then from the stored turn.
/// Kept for as long as the run's snapshot is.
/// </summary>
public interface IRunTraceStore
{
    /// <summary>Appends one trace event of the run; events arrive in sequence order.</summary>
    Task AppendAsync(string runId, Maf.Lab.Domain.Tracing.TraceEvent traceEvent, CancellationToken ct);

    /// <summary>The run's trace events with a sequence number above <paramref name="afterSeq"/>, in order.</summary>
    Task<IReadOnlyList<Maf.Lab.Domain.Tracing.TraceEvent>> ReadAsync(string runId, int afterSeq, CancellationToken ct);
}

/// <summary>What was answered under an idempotency key, so the same call is answered rather than done twice.</summary>
/// <param name="RequestDigest">Of the request that was answered, so a key reused for something else is caught.</param>
public sealed record IdempotentAnswer(string Key, string RequestDigest, string Answer, DateTimeOffset At);

/// <summary>Why a key could not be used for this call.</summary>
public enum IdempotencyOutcome
{
    /// <summary>Nothing has been done under this key: the caller should do the work and record the answer.</summary>
    Fresh,

    /// <summary>The same call was answered before; the answer is the one to give.</summary>
    Replay,

    /// <summary>A different call was answered under this key; this one is refused rather than answered wrongly.</summary>
    Conflict,
}

public interface IIdempotencyStore
{
    /// <summary>What to do about a call arriving under this key.</summary>
    Task<(IdempotencyOutcome Outcome, IdempotentAnswer? Answer)> CheckAsync(
        string firmId, string key, string requestDigest, CancellationToken ct);

    /// <summary>Records the answer given, so a repeat is replayed rather than done again.</summary>
    Task RecordAsync(string firmId, string key, string requestDigest, string answer, CancellationToken ct);
}

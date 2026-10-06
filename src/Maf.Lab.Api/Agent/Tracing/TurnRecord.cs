using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>
/// The turn's core record (introduce-plugins decision 7): the subset of its trace the core keeps with the turn, in the
/// trace's own documented shape, whatever plugin is installed — what the answer check's previous read and the
/// statistics need. Content-free by spec but for the envelopes, which hold what the model was handed (as the turn's
/// sources do) and live, capped like every trace field, as long as the conversation. The full trace — the model
/// capture, the prompt, retrieval diagnostics, the answer and reasoning deltas — is only an observer's (the monitor's).
/// </summary>
public static class TurnRecord
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
    {
        TraceKinds.Intent, TraceKinds.Domain, TraceKinds.Boundary, TraceKinds.Guardrail, TraceKinds.Relevance,
        TraceKinds.AnswerCheck, TraceKinds.Signals, TraceKinds.Sources, TraceKinds.Audit, TraceKinds.Focus,
        TraceKinds.TurnEnd, TraceKinds.Envelope,
    };
}

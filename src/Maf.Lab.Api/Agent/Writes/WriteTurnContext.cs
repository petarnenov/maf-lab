using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Api.Agent.Writes;

/// <summary>
/// Who and where a write flow is acting for, set by the core before it hands a flow a proposal or a resolution, so the
/// ports a flow uses (<see cref="CoreWriteAudit"/>, <see cref="CoreConsultationScreening"/>, <see cref="CoreWriteTraceStep"/>)
/// need no parameter for it — the principal in particular comes from the request, never from a flow. One per request.
/// </summary>
public sealed class WriteTurnContext
{
    public Principal? Principal { get; private set; }
    public string? ConversationId { get; private set; }
    public string? TurnId { get; private set; }
    public string CallId { get; private set; } = "";

    /// <summary>The turn's trace, when a turn is running; an answer outside a turn has none.</summary>
    public TurnTrace? Trace { get; private set; }

    public void Set(Principal principal, string? conversationId, string? turnId, string callId, TurnTrace? trace)
    {
        (Principal, ConversationId, TurnId, CallId, Trace) = (principal, conversationId, turnId, callId, trace);
    }
}

using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Agent.Writes;

/// <summary>The audit chain, as a write flow records its steps (identifiers only; the actor is the request's).</summary>
public sealed class CoreWriteAudit(ToolAudit audit, WriteTurnContext context, ILogger<CoreWriteAudit> logger) : IWriteAudit
{
    public async Task RecordAsync(string kind, string step, string identifiers, string outcome, CancellationToken ct)
    {
        if (context.Principal is not { } principal)
        {
            return;
        }
        try
        {
            await audit.RecordAsync(new AuditEntry(principal, context.ConversationId, context.TurnId, step, identifiers, outcome, 0, kind), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("write audit failed ({Error})", ex.GetType().Name);
        }
    }
}

/// <summary>The guardrail on a reviewer's words, in the current turn's trace.</summary>
public sealed class CoreConsultationScreening(Guardrail guardrail, WriteTurnContext context) : IConsultationScreening
{
    public Task<ConsultationResult> ScreenAsync(ConsultationResult result, CancellationToken ct) =>
        guardrail.ScreenConsultationAsync(result, context.Trace ?? new TurnTrace(null), context.CallId, ct);
}

/// <summary>A step in the current turn's trace; outside a turn there is none to add it to.</summary>
public sealed class CoreWriteTraceStep(WriteTurnContext context) : IWriteTraceStep
{
    public void Record(string kind, string title, JsonObject data)
    {
        // The tool call it belongs to is the core's to name, as every other step of the turn does.
        data["callId"] ??= context.CallId;
        context.Trace?.Add(kind, title, data);
    }
}

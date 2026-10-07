using System.Text.Json;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Agent.Writes;

/// <summary>What a turn does with a write a tool asked a person about.</summary>
public abstract record WriteStep
{
    /// <summary>Put it to the person: the run pauses on this question.</summary>
    public sealed record Ask(PendingWrite Write, PersonQuestion Question) : WriteStep;

    /// <summary>Nothing is put to the person now; the model is told this and answers in its own words.</summary>
    public sealed record Tell(string Message) : WriteStep;
}

/// <summary>
/// Between a write tool's request for input and the person (generalize-write-confirmation): the core's half of the seam.
/// It finds the tool's flow, hands it the proposal with the conversation's proposals of that tool still waiting for
/// input, and keeps what the flow decided in the one pending-writes store. It reads none of the summary's fields.
/// </summary>
public sealed class WriteConfirmations(
    WriteFlows flows,
    WriteTurnContext context,
    IDbContextFactory<MafDbContext> db,
    ToolAudit audit,
    TimeProvider time,
    ILogger<WriteConfirmations> logger)
{
    /// <summary>What the model is told when a write tool has no flow: nobody can be asked, so nothing will happen.</summary>
    public const string NoFlow =
        "This change cannot be confirmed here, so it has not been proposed to anyone and nothing has been changed. " +
        "Tell the user it is not available.";

    public async Task<WriteStep> ProposedAsync(Principal principal, string conversationId, string turnId, string callId,
        string toolName, string personMessage, CapturedConfirmation captured, TurnTrace trace, CancellationToken ct)
    {
        context.Set(principal, conversationId, turnId, callId, trace);
        if (flows.For(toolName) is not { } flow)
        {
            await RefusedAsync(principal, conversationId, turnId, toolName, ct);
            return new WriteStep.Tell(NoFlow);
        }

        await using var store = await db.CreateDbContextAsync(ct);
        var open = await store.PendingWrites.AsNoTracking()
            .Where(p => p.ConversationId == conversationId && p.TenantId == principal.TenantId.Value && p.UserId == principal.UserId
                && p.ToolName == toolName && p.Status == PendingWriteStatus.AwaitingInput)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(5)
            .ToListAsync(ct);

        var writeId = $"w_{Guid.NewGuid():N}";
        var proposal = new WriteProposal(writeId, toolName, captured.Summary, null, principal, conversationId, turnId,
            [.. open.Select(o => new OpenWriteInput(o.Id, Parse(o.Summary), o.FlowJson))], personMessage);
        var outcome = await flow.ProposedAsync(proposal, ct);

        var (status, flowJson) = outcome switch
        {
            WriteFlowOutcome.AskPerson ask => (PendingWriteStatus.AwaitingConfirmation, ask.FlowJson),
            WriteFlowOutcome.AskInput input => (PendingWriteStatus.AwaitingInput, input.FlowJson),
            WriteFlowOutcome.TellModel { Refused: true } => (PendingWriteStatus.Refused, null),
            _ => (PendingWriteStatus.Failed, (string?)null),
        };
        var now = time.GetUtcNow().UtcDateTime;
        var row = new PendingWriteRow
        {
            Id = writeId,
            TenantId = principal.TenantId.Value,
            UserId = principal.UserId,
            ConversationId = conversationId,
            TurnId = turnId,
            ToolName = toolName,
            State = captured.State,
            Summary = captured.Summary.GetRawText(),
            Question = outcome is WriteFlowOutcome.AskPerson { Question: { } asked } ? asked : captured.Question,
            ExpiresAt = captured.ExpiresAt?.UtcDateTime,
            Status = status,
            FlowJson = flowJson,
            CreatedAt = now,
            UpdatedAt = now,
        };
        store.PendingWrites.Add(row);
        await store.SaveChangesAsync(ct);

        switch (outcome)
        {
            case WriteFlowOutcome.AskPerson:
                var write = PendingWrite.From(row, flow.SummarySchema);
                return new WriteStep.Ask(write, write.ToQuestion(callId, captured.AnswerSchema));
            case WriteFlowOutcome.AskInput input:
                return new WriteStep.Tell(input.MessageToModel);
            default:
                // Refused or failed: this proposal is over, and so is anything of the same tool still waiting for input.
                await ResolveOpenInputsAsync(row, status, ct);
                return new WriteStep.Tell(((WriteFlowOutcome.TellModel)outcome).Message);
        }
    }

    /// <summary>
    /// Once a proposal is resolved, the proposals of the same tool still waiting for input in that conversation are over
    /// too: they were the steps towards it. A core rule, not a flow's.
    /// </summary>
    public async Task ResolveOpenInputsAsync(PendingWriteRow row, string status, CancellationToken ct)
    {
        await using var store = await db.CreateDbContextAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        await store.PendingWrites
            .Where(p => p.ConversationId == row.ConversationId && p.TenantId == row.TenantId && p.UserId == row.UserId
                && p.ToolName == row.ToolName && p.Id != row.Id && p.Status == PendingWriteStatus.AwaitingInput)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status).SetProperty(p => p.UpdatedAt, now), ct);
    }

    private async Task RefusedAsync(Principal principal, string conversationId, string turnId, string toolName, CancellationToken ct)
    {
        try
        {
            await audit.RecordAsync(new AuditEntry(principal, conversationId, turnId, "write.refused", $"tool={toolName}", "no_flow", 0,
                "write"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("write audit failed ({Error})", ex.GetType().Name);
        }
    }

    internal static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }
}

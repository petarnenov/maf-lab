using System.Text.Json;
using System.Threading.Channels;
using AGUI.Abstractions;
using Maf.Lab.Api.Agent.Streaming;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Agent;

/// <summary>What became of a person's answer.</summary>
public abstract record ConfirmationOutcome
{
    public sealed record Applied(FeeAdjustmentOutcomeDto Result) : ConfirmationOutcome;

    public sealed record Rejected(string AdjustmentId) : ConfirmationOutcome;

    /// <summary>No such proposal is waiting for this person.</summary>
    public sealed record NotFound : ConfirmationOutcome;

    public sealed record Failed(string Message) : ConfirmationOutcome;
}

/// <summary>What the tool reported back. Mirrors the tool's own result shape, which the API only passes on.</summary>
public sealed record FeeAdjustmentOutcomeDto(string Status, FeeAdjustmentApplied? Adjustment, string Message);

/// <summary>
/// A person's answer to a proposal. The answer travels back to the server with the state the proposal was
/// issued with, so what executes is what was put to them — not what anything has said since.
/// </summary>
public sealed class ConfirmationService(
    IToolSource tools,
    FeeAdjustmentFlow flow,
    IDbContextFactory<MafDbContext> db,
    TimeProvider time,
    ILogger<ConfirmationService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ConfirmationOutcome> AnswerAsync(
        Principal principal, string bearerToken, string conversationId, string adjustmentId, bool approve, CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        var row = await context.PendingAdjustments.FirstOrDefaultAsync(p => p.Id == adjustmentId, ct);

        // A proposal belongs to the person it was put to. Anyone else is told only that there is nothing here.
        if (row is null
            || row.FirmId != principal.FirmId.Value
            || row.UserId != principal.UserId
            || row.ConversationId != conversationId
            || row.Status != PendingAdjustmentStatus.AwaitingConfirmation)
        {
            return new ConfirmationOutcome.NotFound();
        }

        var summary = JsonSerializer.Deserialize<FeeAdjustmentSummary>(row.Summary, Json);
        if (summary is null)
        {
            return new ConfirmationOutcome.Failed("That proposal can no longer be read. Propose the adjustment again.");
        }

        await flow.RecordAsync(principal, conversationId, row.TurnId,
            approve ? FeeAdjustmentFlow.Confirmed : FeeAdjustmentFlow.Rejected, summary,
            approve ? "approved" : "rejected", 0, ct);

        if (!approve)
        {
            await ResolveAsync(row.Id, PendingAdjustmentStatus.Declined, ct);
            return new ConfirmationOutcome.Rejected(row.Id);
        }

        await using var set = await tools.GetToolsAsync(bearerToken, null, ct);
        if (set.Confirm is not { } confirm)
        {
            return new ConfirmationOutcome.Failed("The billing tools are unavailable; nothing has been changed.");
        }

        ModelContextProtocol.Protocol.CallToolResult result;
        try
        {
            result = await confirm(
                row.ToolName,
                new Dictionary<string, object?>
                {
                    // Ignored by the server, which executes the state; sent because the tool declares them.
                    ["accountId"] = summary.AccountId,
                    ["amount"] = summary.Amount,
                    ["reason"] = "confirmed by the advisor",
                },
                row.State,
                approve: true,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("confirmation call failed ({Error})", ex.GetType().Name);
            await flow.RecordAsync(principal, conversationId, row.TurnId, FeeAdjustmentFlow.Applied, summary, "error", 0, ct);
            return new ConfirmationOutcome.Failed("The adjustment could not be applied; nothing has been changed.");
        }

        if (result.IsError == true || result.StructuredContent is not { } structured)
        {
            await flow.RecordAsync(principal, conversationId, row.TurnId, FeeAdjustmentFlow.Applied, summary, "error", 0, ct);
            await ResolveAsync(row.Id, PendingAdjustmentStatus.Failed, ct);
            return new ConfirmationOutcome.Failed(Text(result));
        }

        var outcome = JsonSerializer.Deserialize<FeeAdjustmentOutcomeDto>(structured.GetRawText(), Json)
            ?? new FeeAdjustmentOutcomeDto("applied", null, "Applied.");
        await flow.RecordAsync(principal, conversationId, row.TurnId, FeeAdjustmentFlow.Applied, summary, outcome.Status, 0, ct);
        await ResolveAsync(row.Id, PendingAdjustmentStatus.Applied, ct);
        return new ConfirmationOutcome.Applied(outcome);
    }

    /// <summary>
    /// A person's answer arriving as a run that resumes the interrupt the previous run stopped for. The run
    /// reports what happened and ends; underneath, nothing about applying an adjustment has changed.
    /// </summary>
    public async Task ResumeAsync(
        Principal principal, string bearerToken, string conversationId, string runId, AGUIResume resume,
        ChannelWriter<BaseEvent> events, CancellationToken ct)
    {
        await events.WriteAsync(new RunStartedEvent { ThreadId = conversationId, RunId = runId }, ct);

        var approve = Approved(resume);
        var outcome = await AnswerAsync(principal, bearerToken, conversationId, resume.InterruptId ?? "", approve, ct);

        var messageId = $"m_{Guid.NewGuid():N}";
        var text = outcome switch
        {
            ConfirmationOutcome.Applied applied => applied.Result.Message,
            ConfirmationOutcome.Rejected => "Nothing was applied. The advisor declined the adjustment.",
            ConfirmationOutcome.NotFound => "That proposal is no longer waiting for an answer.",
            _ => ((ConfirmationOutcome.Failed)outcome).Message,
        };

        await events.WriteAsync(new TextMessageStartEvent { MessageId = messageId, Role = AGUIRoles.Assistant }, ct);
        await events.WriteAsync(new TextMessageContentEvent { MessageId = messageId, Delta = text }, ct);
        await events.WriteAsync(new TextMessageEndEvent { MessageId = messageId }, ct);
        await events.WriteAsync(new RunFinishedEvent
        {
            ThreadId = conversationId,
            RunId = runId,
            Outcome = new RunFinishedSuccessOutcome(),
        }, ct);
    }

    /// <summary>An answer is an approval only when it says so. Anything else leaves the fee where it is.</summary>
    private static bool Approved(AGUIResume resume)
    {
        if (resume.Payload is not { } payload)
        {
            return false;
        }
        if (payload.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("approve", out var approve)
            && approve.ValueKind == JsonValueKind.True;
    }

    private async Task ResolveAsync(string id, string status, CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        var row = await context.PendingAdjustments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
        {
            return;
        }
        row.Status = status;
        row.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(ct);
    }

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result) =>
        result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault()?.Text
        ?? "The adjustment could not be applied; nothing has been changed.";
}

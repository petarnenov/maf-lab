using System.Text.Json;
using System.Threading.Channels;
using Maf.Lab.Api.Agent.Writes;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>What became of a person's answer.</summary>
public abstract record ConfirmationOutcome
{
    /// <summary>The tool applied it; its own status (e.g. <c>applied</c>, <c>already_applied</c>) and words.</summary>
    public sealed record Applied(string Status, string Message) : ConfirmationOutcome;

    public sealed record Rejected(string WriteId) : ConfirmationOutcome;

    /// <summary>It was past its expiry when the answer arrived: it must be proposed again.</summary>
    public sealed record Expired(string WriteId) : ConfirmationOutcome;

    /// <summary>No such proposal is waiting for this person.</summary>
    public sealed record NotFound : ConfirmationOutcome;

    public sealed record Failed(string Message) : ConfirmationOutcome;
}

/// <summary>
/// A person's answer to a write waiting for them, whichever tool proposed it (generalize-write-confirmation). The
/// answer travels back to the server with the state the proposal was issued with, so what executes is what was put to
/// them — not what anything has said since. Every change of the proposal's status is one guarded update from the status
/// it is expected to have, so whichever replica takes an answer, it is taken once.
/// </summary>
public sealed class ConfirmationService(
    IToolSource tools,
    WriteFlows flows,
    WriteConfirmations writes,
    WriteTurnContext context,
    IDbContextFactory<MafDbContext> db,
    TimeProvider time,
    ILogger<ConfirmationService> logger)
{
    public const string NoLongerAvailable = "That change can no longer be confirmed here; nothing has been changed.";

    public async Task<ConfirmationOutcome> AnswerAsync(
        Principal principal, string bearerToken, string conversationId, string writeId, bool approve,
        string? idempotencyKey, CancellationToken ct)
    {
        await using var store = await db.CreateDbContextAsync(ct);
        var row = await store.PendingWrites.AsNoTracking().FirstOrDefaultAsync(p => p.Id == writeId, ct);

        // A proposal belongs to the person it was put to. Anyone else is told only that there is nothing here.
        if (row is null
            || row.TenantId != principal.TenantId.Value
            || row.UserId != principal.UserId
            || row.ConversationId != conversationId
            || row.Status != PendingWriteStatus.AwaitingConfirmation)
        {
            return new ConfirmationOutcome.NotFound();
        }

        context.Set(principal, conversationId, row.TurnId, "", null);
        var flow = flows.For(row.ToolName);
        var proposal = new WriteProposal(row.Id, row.ToolName, WriteConfirmations.Parse(row.Summary), row.FlowJson, principal,
            conversationId, row.TurnId, []);

        if (row.ExpiresAt is { } expiry && expiry <= time.GetUtcNow().UtcDateTime)
        {
            return await ResolveAsync(row, PendingWriteStatus.Expired, ct)
                ? await ResolvedAsync(flow, proposal, row, WriteResolution.Expired, "expired", new ConfirmationOutcome.Expired(row.Id), ct)
                : new ConfirmationOutcome.NotFound();
        }

        if (!approve)
        {
            return await ResolveAsync(row, PendingWriteStatus.Declined, ct)
                ? await ResolvedAsync(flow, proposal, row, WriteResolution.Declined, "rejected", new ConfirmationOutcome.Rejected(row.Id), ct)
                : new ConfirmationOutcome.NotFound();
        }

        if (flow is null)
        {
            return new ConfirmationOutcome.Failed(NoLongerAvailable);
        }
        await using var set = await tools.GetToolsAsync(bearerToken, null, ct);
        if (set.Confirm is not { } confirm)
        {
            return new ConfirmationOutcome.Failed("The tools are unavailable; nothing has been changed.");
        }

        ModelContextProtocol.Protocol.CallToolResult result;
        try
        {
            // The server executes the state; these are what the tool declares, sent as the flow says, and not believed.
            result = await confirm(row.ToolName, flow.ConfirmArguments(proposal.Summary), row.State, approve: true, idempotencyKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("confirmation call failed ({Error})", ex.GetType().Name);
            // Not resolved: the proposal is still waiting, and sending the answer again is safe.
            await flow.ResolvedAsync(proposal, WriteResolution.Failed, "error", ct);
            return new ConfirmationOutcome.Failed("The change could not be applied; nothing has been changed.");
        }

        if (result.IsError == true || result.StructuredContent is not { } structured)
        {
            await ResolveAsync(row, PendingWriteStatus.Failed, ct);
            return await ResolvedAsync(flow, proposal, row, WriteResolution.Failed, "error", new ConfirmationOutcome.Failed(Text(result)), ct);
        }

        var status = Str(structured, "status") ?? "applied";
        await ResolveAsync(row, PendingWriteStatus.Applied, ct);
        return await ResolvedAsync(flow, proposal, row, WriteResolution.Applied, status,
            new ConfirmationOutcome.Applied(status, Str(structured, "message") ?? "Applied."), ct);
    }

    /// <summary>
    /// A person's answer arriving as a run that resumes the interrupt the previous run stopped for. The run reports what
    /// happened, as the assistant's words, and ends; underneath, nothing about applying an adjustment has changed.
    /// </summary>
    public async Task ResumeAsync(
        Principal principal, string bearerToken, string conversationId, PersonAnswer answer,
        ChannelWriter<ChatResponseUpdate> output, CancellationToken ct)
    {
        var outcome = await AnswerAsync(principal, bearerToken, conversationId, answer.QuestionId, Approved(answer.Payload),
            IdempotencyKeyOf(answer.Payload), ct);

        var text = outcome switch
        {
            ConfirmationOutcome.Applied applied => applied.Message,
            ConfirmationOutcome.Rejected => "Nothing was applied. The advisor declined the change.",
            ConfirmationOutcome.Expired => "That proposal is too old to apply. Propose it again.",
            ConfirmationOutcome.NotFound => "That proposal is no longer waiting for an answer.",
            _ => ((ConfirmationOutcome.Failed)outcome).Message,
        };
        await output.WriteAsync(new ChatResponseUpdate(ChatRole.Assistant, text) { MessageId = $"m_{Guid.NewGuid():N}" }, ct);
    }

    /// <summary>
    /// The caller's own idempotency key, when it sent one. It is the client's way of saying "this is the same
    /// attempt", which is what makes sending an interrupted call again safe.
    /// </summary>
    private static string? IdempotencyKeyOf(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.Object } p
        && p.TryGetProperty("idempotencyKey", out var key) && key.ValueKind == JsonValueKind.String
            ? key.GetString()
            : null;

    /// <summary>An answer is an approval only when it says so. Anything else leaves the fee where it is.</summary>
    private static bool Approved(JsonElement? payload) =>
        payload is { } p
        && (p.ValueKind == JsonValueKind.True
            || p.ValueKind == JsonValueKind.Object && p.TryGetProperty("approve", out var approve) && approve.ValueKind == JsonValueKind.True);

    /// <summary>One guarded update from waiting: false when someone else resolved it first.</summary>
    private async Task<bool> ResolveAsync(PendingWriteRow row, string status, CancellationToken ct)
    {
        await using var store = await db.CreateDbContextAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        return await store.PendingWrites
            .Where(p => p.Id == row.Id && p.Status == PendingWriteStatus.AwaitingConfirmation)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status).SetProperty(p => p.UpdatedAt, now), ct) == 1;
    }

    /// <summary>The flow records its own step, and what was waiting for input on the way to this proposal is over too.</summary>
    private async Task<ConfirmationOutcome> ResolvedAsync(IWriteConfirmationFlow? flow, WriteProposal proposal, PendingWriteRow row,
        WriteResolution resolution, string outcome, ConfirmationOutcome answer, CancellationToken ct)
    {
        if (flow is not null)
        {
            await flow.ResolvedAsync(proposal, resolution, outcome, ct);
        }
        await writes.ResolveOpenInputsAsync(row, resolution switch
        {
            WriteResolution.Applied => PendingWriteStatus.Applied,
            WriteResolution.Declined => PendingWriteStatus.Declined,
            WriteResolution.Expired => PendingWriteStatus.Expired,
            _ => PendingWriteStatus.Failed,
        }, ct);
        return answer;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result) =>
        result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault()?.Text
        ?? "The change could not be applied; nothing has been changed.";
}

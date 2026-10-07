using System.Text.Json;
using System.Threading.Channels;
using Maf.Lab.Api.Agent.AGUI;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// Coming back to a run the caller lost (agui-protocol-only): the official server does not serve the protocol's
/// <c>connect</c>, so a rejoin is a run on the same thread, naming the lost run as its parent and saying nothing new. It
/// is told, as the protocol's own events, what the lost run had said and done by its last snapshot — the answer so far,
/// each tool call and how it ended — and, when that run stopped for a person, it ends paused on the same question.
/// Ownership was checked before the run started (<see cref="AGUI.ChatRunFilter"/>).
/// </summary>
public sealed class RunRejoin(IRunStateStore runs, IDbContextFactory<MafDbContext> db, Writes.WriteFlows flows,
    Writes.WriteConfirmations writes, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Whether <paramref name="runId"/> is a run of this caller's thread that is still kept.</summary>
    public async Task<RunState?> FindAsync(Principal principal, string threadId, string runId, CancellationToken ct) =>
        await runs.GetAsync(runId, ct) is { } state
        && state.UserId == principal.UserId && state.TenantId == principal.TenantId.Value && state.ConversationId == threadId
            ? state
            : null;

    public async Task ReplayAsync(Principal principal, string threadId, string runId, ChannelWriter<ChatResponseUpdate> output,
        CancellationToken ct)
    {
        var state = await FindAsync(principal, threadId, runId, ct)
            ?? throw new InvalidOperationException("The run is not available.");

        var messageId = $"m_{runId}";
        foreach (var call in state.ToolCalls)
        {
            await WriteAsync(output, new FunctionCallContent(call.CallId, call.ToolName, Arguments(call.ArgumentSummary)), ct);
            if (call.Finished)
            {
                await WriteAsync(output, new FunctionResultContent(call.CallId,
                    JsonSerializer.Serialize(new { tool = call.ToolName, summary = call.ResultSummary, isError = call.IsError }, Json)), ct);
            }
        }
        if (state.Answer.Length > 0)
        {
            await output.WriteAsync(new ChatResponseUpdate(ChatRole.Assistant, state.Answer) { MessageId = messageId }, ct);
        }
        if (state.Outcome == RunOutcomes.AwaitingPerson && state.AwaitingId is { } waiting
            && await QuestionAsync(principal, waiting, state, ct) is { } question)
        {
            await WriteAsync(output, TurnContents.Ask(question), ct);
        }
    }

    /// <summary>
    /// The question the lost run stopped on, rebuilt from the proposal it is about by the same builder the turn used. A
    /// proposal past its expiry is recorded as expired and nothing is replayed.
    /// </summary>
    private async Task<PersonQuestion?> QuestionAsync(Principal principal, string writeId, RunState state, CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        var row = await context.PendingWrites.AsNoTracking().FirstOrDefaultAsync(p => p.Id == writeId
            && p.UserId == principal.UserId && p.TenantId == principal.TenantId.Value
            && p.Status == PendingWriteStatus.AwaitingConfirmation, ct);
        if (row is null)
        {
            return null;
        }
        var now = time.GetUtcNow().UtcDateTime;
        if (row.ExpiresAt is { } expiry && expiry <= now)
        {
            var expired = await context.PendingWrites
                .Where(p => p.Id == row.Id && p.Status == PendingWriteStatus.AwaitingConfirmation)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PendingWriteStatus.Expired).SetProperty(p => p.UpdatedAt, now), ct);
            if (expired == 1 && flows.For(row.ToolName) is { } flow)
            {
                await flow.ResolvedAsync(new Maf.Lab.Plugins.Abstractions.WriteProposal(row.Id, row.ToolName, Writes.WriteConfirmations.Parse(row.Summary),
                    row.FlowJson, principal, row.ConversationId, row.TurnId, []), Maf.Lab.Plugins.Abstractions.WriteResolution.Expired, "expired", ct);
                await writes.ResolveOpenInputsAsync(row, PendingWriteStatus.Expired, ct);
            }
            return null;
        }
        var callId = state.ToolCalls.LastOrDefault(c => c.ToolName == row.ToolName)?.CallId ?? "";
        return Writes.PendingWrite.From(row, flows.SchemaFor(row.ToolName)).ToQuestion(callId, null);
    }

    /// <summary>The identifier-only arguments back from their summary ("accountId=A-1043 runId=4417").</summary>
    private static Dictionary<string, object?> Arguments(string summary) =>
        summary.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0], kv => (object?)kv[1], StringComparer.Ordinal);

    private static ValueTask WriteAsync(ChannelWriter<ChatResponseUpdate> output, AIContent content, CancellationToken ct) =>
        output.WriteAsync(new ChatResponseUpdate(ChatRole.Assistant, [content]), ct);
}

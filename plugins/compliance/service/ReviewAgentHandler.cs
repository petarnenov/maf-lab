using System.Text.Json;
using A2A;
using Maf.Lab.A2A;
using Microsoft.Extensions.Options;
using MessageRole = A2A.Role;

namespace Maf.Lab.ComplianceAgent;

/// <summary>
/// The review, as a caller experiences it: a task that reports progress, takes long enough to be treated as work
/// in progress, sometimes stops to ask for the advisor's justification, and ends with a structured verdict.
///
/// Nothing here decides anything real. The verdict is a threshold and a stopwatch; it says so on the card, in the
/// artifact, and here.
/// </summary>
public sealed class ReviewAgentHandler(
    IPartnerAccessor partners,
    IOptions<ReviewOptions> options,
    ITaskStore tasks,
    IOptions<A2AOptions> a2a,
    TimeProvider time,
    ILogger<ReviewAgentHandler> logger) : IAgentHandler
{
    /// <summary>What a caller is told when it asks the reviewer something the reviewer does not do.</summary>
    public const string OutOfScope = "This agent only reviews fee adjustments.";

    /// <summary>What the reviewer asks for when it wants a justification before deciding.</summary>
    public const string JustificationQuestion =
        "Why is this adjustment being made? Send the advisor's justification to continue this review.";

    public const string ArtifactName = "compliance-verdict";

    private static readonly string[] Stages =
        ["Reading the adjustment", "Checking the firm's fee schedule", "Checking recent adjustments", "Forming a verdict"];

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        var partner = partners.Current;
        var updater = new TaskUpdater(queue, context.TaskId, context.ContextId);
        var adjustment = Adjustment.From(context);

        if (adjustment is null)
        {
            await queue.EnqueueMessageAsync(Say(OutOfScope), cancellationToken);
            queue.Complete();
            return;
        }

        if (!context.IsContinuation)
        {
            await updater.SubmitAsync(cancellationToken);
        }
        else if (context.Task is { } current)
        {
            await queue.EnqueueTaskAsync(current, cancellationToken);
        }

        // Asked once, never twice: a review that has already asked carries the question in its history.
        if (!context.IsContinuation && ShouldAsk())
        {
            await updater.RequireInputAsync(Say(JustificationQuestion), cancellationToken);
            return;
        }

        logger.LogInformation("review started partner={Partner} task={TaskId}", partner.PartnerId, context.TaskId);
        // A cancel may arrive at the other replica; the shared store is where it is recorded, so the review watches it
        // there (stop-anything) and stops at its next step, whichever replica took the cancel.
        await using var watch = TaskCancelWatch.Start(tasks, context.TaskId, TimeSpan.FromMilliseconds(a2a.Value.CancelPollMs), time);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watch.Token);
        try
        {
            await ReviewAsync(adjustment, updater, work.Token);
        }
        catch (OperationCanceledException) when (watch.Canceled)
        {
            logger.LogInformation("review canceled through the store task={TaskId}", context.TaskId);
            throw;
        }
    }

    private async Task ReviewAsync(Adjustment adjustment, TaskUpdater updater, CancellationToken cancellationToken)
    {
        var step = TimeSpan.FromMilliseconds(Duration() / Stages.Length);
        foreach (var stage in Stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await updater.StartWorkAsync(Say($"{stage}…"), cancellationToken);
            await System.Threading.Tasks.Task.Delay(step, time, cancellationToken);
        }

        // Size, not direction: a credit that large is as much a reason to refuse as an increase that large.
        var refused = Math.Abs(adjustment.Amount) > options.Value.RefuseAboveAmount;
        var reason = refused
            ? $"The adjustment exceeds the {options.Value.RefuseAboveAmount:0.##} review threshold and needs a human reviewer."
            : "Within the review threshold and consistent with the firm's recent adjustments.";

        await updater.AddArtifactAsync(
            [
                new Part
                {
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        adjustmentId = adjustment.AdjustmentId,
                        // The account is echoed so the caller can tell that this verdict answers its question.
                        accountId = adjustment.AccountId,
                        decision = refused ? "refused" : "approved",
                        reason,
                        reviewedAt = time.GetUtcNow().UtcDateTime,
                        simulated = true,
                    }),
                },
            ],
            name: ArtifactName, cancellationToken: cancellationToken);
        await updater.CompleteAsync(Say(refused ? "Refused." : "Approved."), cancellationToken);
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken) =>
        await new TaskUpdater(queue, context.TaskId, context.ContextId).CancelAsync(cancellationToken);

    private bool ShouldAsk() => Random.Shared.NextDouble() < options.Value.AskForJustificationRate;

    private int Duration()
    {
        var (min, max) = (options.Value.MinDurationMs, Math.Max(options.Value.MinDurationMs, options.Value.MaxDurationMs));
        return min == max ? min : Random.Shared.Next(min, max);
    }

    private static Message Say(string text) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = MessageRole.Agent,
        Parts = [new Part { Text = text }],
    };
}

/// <param name="AdjustmentId">The caller's identifier for the adjustment; it is echoed in the verdict.</param>
internal sealed record Adjustment(string AdjustmentId, string FirmId, string AccountId, decimal Amount)
{
    /// <summary>
    /// Reads the adjustment from the request's data part — the caller is a program, so the structured part is the
    /// contract. A review continuing after a question finds it in the task's history instead.
    /// </summary>
    public static Adjustment? From(RequestContext context)
    {
        var parts = (context.Message?.Parts ?? []).Concat(
            context.Task?.History?.SelectMany(message => message.Parts ?? []) ?? []);
        foreach (var part in parts)
        {
            if (part.Data is { } data && Read(data) is { } adjustment)
            {
                return adjustment;
            }
        }
        return null;
    }

    private static Adjustment? Read(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("amount", out var amount))
        {
            return null;
        }
        return new Adjustment(
            Text(data, "adjustmentId") ?? "unknown",
            Text(data, "firmId") ?? "unknown",
            Text(data, "accountId") ?? "unknown",
            amount.ValueKind is JsonValueKind.Number ? amount.GetDecimal()
                : decimal.TryParse(amount.GetString(), out var parsed) ? parsed : 0m);
    }

    private static string? Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

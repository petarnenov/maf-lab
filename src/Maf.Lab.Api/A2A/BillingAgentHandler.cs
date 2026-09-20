using System.Text.RegularExpressions;
using A2A;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Billing;
using Microsoft.Extensions.Options;
// Both libraries have a Role; naming them apart is clearer than hoping the right one wins.
using MessageRole = A2A.Role;
using UserRole = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.Api.A2A;

/// <summary>
/// What a partner system actually gets when it talks to us. Two shapes of work, as the protocol expects: a
/// question is answered with a message, and work that takes time becomes a task the caller can follow, cancel,
/// or be asked a question by.
///
/// Starting a billing run is **simulated**: it walks the real lifecycle over seeded data and bills nobody. The
/// skill description says so, the artifact says so, and the README says so.
/// </summary>
public sealed partial class BillingAgentHandler(
    IPartnerAccessor partners,
    BillingSeedStore billing,
    IOptions<A2AOptions> options,
    ToolAudit audit,
    AssistantBridge assistant,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<BillingAgentHandler> logger) : IAgentHandler
{
    /// <summary>What a caller is told when it asks about a firm it is not entitled to — the same sentence, always.</summary>
    public const string OutOfScope = "This request concerns data outside your entitlement.";

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        var partner = partners.Current;
        var text = context.UserText ?? "";
        var started = time.GetUtcNow();
        var updater = new TaskUpdater(queue, context.TaskId, context.ContextId);

        try
        {
            if (WantsToStartARun(text) || context.IsContinuation)
            {
                await RunBillingAsync(partner, context, queue, updater, text, cancellationToken);
                return;
            }
            await AnswerAsync(partner, queue, text, cancellationToken);
        }
        finally
        {
            await RecordAsync(partner, context.IsContinuation ? "continue" : "message", context.TaskId, started, cancellationToken);
        }
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow();
        await new TaskUpdater(queue, context.TaskId, context.ContextId).CancelAsync(cancellationToken);
        await RecordAsync(partners.Current, "cancel", context.TaskId, started, cancellationToken);
    }

    /// <summary>
    /// A question that can be answered now is answered now — no task, as the protocol allows. A question about a
    /// named run is answered from the run's own record, because the entitlement check is the answer; anything else
    /// goes to the assistant itself, which is where the documentation and the reasoning live.
    /// </summary>
    private async Task AnswerAsync(PartnerPrincipal partner, AgentEventQueue queue, string text, CancellationToken ct)
    {
        if (RunReference().Match(text) is { Success: true } match)
        {
            await queue.EnqueueMessageAsync(
                new Message
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = MessageRole.Agent,
                    Parts = [new Part { Text = StatusFor(partner, match.Groups["run"].Value) }],
                }, ct);
            queue.Complete();
            return;
        }

        if (partner.AllowedFirms.Count == 0)
        {
            await queue.EnqueueMessageAsync(
                new Message
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = MessageRole.Agent,
                    Parts = [new Part { Text = OutOfScope }],
                }, ct);
            queue.Complete();
            return;
        }

        await assistant.AnswerAsync(partner.AllowedFirms.First(), text, queue, ct);
    }

    private string StatusFor(PartnerPrincipal partner, string runId)
    {
        // The entitlement decides, and it decides the same way whether or not the run exists: a partner learns
        // nothing about another firm, not even that one of its runs exists.
        foreach (var firm in partner.AllowedFirms)
        {
            var status = billing.GetStatus(PartnerScope(firm), runId);
            if (status is not null)
            {
                return $"Run {status.RunId} for {firm.Value} is {status.Status} "
                    + $"({status.AccountCount} accounts, period {status.PeriodStart:yyyy-MM-dd} to {status.PeriodEnd:yyyy-MM-dd})"
                    + (status.FailureReason is null ? "." : $". Reason: {status.FailureReason}");
            }
        }
        return OutOfScope;
    }

    /// <summary>The simulated run: submitted → working (with progress) → completed, or asked for what is missing.</summary>
    private async Task RunBillingAsync(PartnerPrincipal partner, RequestContext context, AgentEventQueue queue,
        TaskUpdater updater, string text, CancellationToken ct)
    {
        // The task itself is the first event: a non-streaming caller gets it as the result, and a streaming one
        // gets something to attach its later updates to. A continuation already has one, and saying "submitted"
        // again would take it backwards, so it is sent as it stands.
        if (!context.IsContinuation)
        {
            await updater.SubmitAsync(ct);
        }
        else if (context.Task is { } current)
        {
            await queue.EnqueueTaskAsync(current, ct);
        }

        TenantId? firm = FirmReference(text) ?? (partner.AllowedFirms.Count > 0 ? partner.AllowedFirms.First() : null);
        if (firm is not { } scope || !partner.MaySee(scope))
        {
            await updater.RejectAsync(Say(OutOfScope), ct);
            return;
        }

        var period = PeriodReference(text) ?? PeriodReference(HistoryText(context));
        if (period is null)
        {
            // Not an error: the caller simply has not said which period yet, and may say so under this same task.
            await updater.RequireInputAsync(Say("Which period should the run cover? Answer with a month, e.g. 2026-06."), ct);
            return;
        }

        // From here the run belongs to the task, not to the connection that asked for it. A caller whose stream
        // drops must be able to resubscribe and find the run where it left it — which cannot happen if the run
        // died with the request. It stops for the host shutting down, and for an explicit cancel, and for nothing
        // else.
        var work = lifetime.ApplicationStopping;
        await updater.StartWorkAsync(Say($"Starting the billing run for {scope.Value}, period {period}."), work);
        foreach (var step in new[] { "Loading accounts", "Applying fee schedules", "Producing invoices" })
        {
            work.ThrowIfCancellationRequested();
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(options.Value.SimulatedStepMs), work);
            await updater.StartWorkAsync(Say($"{step}…"), work);
        }

        // The artifact reports the firm's most recent seeded run: the lifecycle is what is being built here, not billing.
        var latest = billing.Search(PartnerScope(scope), status: null, periodFrom: null, periodTo: null, limit: 1)
            .Runs.FirstOrDefault();
        await updater.AddArtifactAsync(
            [
                new Part
                {
                    // A DataPart: structured for the caller's code, not prose for a human to parse.
                    Data = System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        firmId = scope.Value,
                        period,
                        runId = latest?.RunId,
                        status = latest?.Status ?? "unknown",
                        accountCount = latest?.AccountCount,
                        simulated = true,
                    }),
                },
            ],
            name: "billing-run-result", cancellationToken: work);
        await updater.CompleteAsync(Say($"The simulated run for {scope.Value} ({period}) is complete."), work);
    }

    private static string HistoryText(RequestContext context) =>
        string.Join(' ', context.Task?.History?.SelectMany(m => m.Parts ?? []).Select(p => p.Text).Where(t => t is not null) ?? []);

    private static Message Say(string text) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = MessageRole.Agent,
        Parts = [new Part { Text = text }],
    };

    /// <summary>A partner reads one firm at a time, with no advisor scope: it is not a user.</summary>
    private static Principal PartnerScope(TenantId firm) => new($"a2a:{firm.Value}", firm, UserRole.READ_ONLY, []);

    private async Task RecordAsync(PartnerPrincipal partner, string operation, string taskId, DateTimeOffset started, CancellationToken ct)
    {
        try
        {
            await audit.RecordAsync(new AuditEntry(
                new Principal(partner.PartnerId,
                    partner.AllowedFirms.Count > 0 ? partner.AllowedFirms.First() : TenantId.Firm("unknown"),
                    UserRole.READ_ONLY, []),
                null, null, $"a2a.{operation}", $"taskId={taskId}", "ok",
                (long)(time.GetUtcNow() - started).TotalMilliseconds, Compliance.AuditKinds.A2ARequest), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("a2a audit write failed ({Error})", ex.GetType().Name);
        }
    }

    private static bool WantsToStartARun(string text) =>
        text.Contains("start", StringComparison.OrdinalIgnoreCase) && text.Contains("run", StringComparison.OrdinalIgnoreCase);

    private static TenantId? FirmReference(string text) =>
        FirmPattern().Match(text) is { Success: true } m ? TenantId.Firm(m.Value.ToLowerInvariant()) : null;

    private static string? PeriodReference(string text) =>
        PeriodPattern().Match(text) is { Success: true } m ? m.Value : null;

    [GeneratedRegex(@"\brun\s*#?\s*(?<run>\d{3,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex RunReference();

    [GeneratedRegex(@"\bfirm-[a-z]\b", RegexOptions.IgnoreCase)]
    private static partial Regex FirmPattern();

    [GeneratedRegex(@"\b20\d{2}-(0[1-9]|1[0-2])\b")]
    private static partial Regex PeriodPattern();
}

using Maf.Lab.A2A;
using System.Text.Json;
using System.Text.RegularExpressions;
using A2A;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
// Both libraries have a Role; naming them apart is clearer than hoping the right one wins.
using MessageRole = A2A.Role;
using UserRole = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.Plugins.A2A;

// names a domain until generalize-a2a-skills moves billing's skills into billing (extract-a2a): run status and the
// simulated run read billing's own server, while billing is installed.
/// <summary>
/// What a partner system actually gets when it talks to us. Two shapes of work, as the protocol expects: a
/// question is answered with a message, and work that takes time becomes a task the caller can follow, cancel,
/// or be asked a question by.
///
/// Starting a billing run is **simulated**: it walks the real lifecycle over seeded data and bills nobody. The
/// skill description says so, the artifact says so, and the README says so.
/// </summary>
public sealed partial class AssistantAgentHandler(
    IPartnerAccessor partners,
    IDomainToolCall tools,
    IOptions<A2AOptions> options,
    IActivityAudit audit,
    IAssistantAnswer assistant,
    IPluginAccess access,
    IInstalledPlugins installed,
    IA2ATaskOwner taskOwners,
    ITaskStore tasks,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<AssistantAgentHandler> logger) : IAgentHandler
{
    /// <summary>What a caller is told when it asks about a firm it is not entitled to — the same sentence, always.</summary>
    public const string OutOfScope = "This request concerns data outside your entitlement.";

    // Run status comes from billing's own server: the literal names the domain until generalize-a2a-skills.
    private const string BillingDomain = "billing";

    /// <summary>The A2A request kind the audit files each partner request under.</summary>
    public const string RequestKind = "a2a.request";

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        var partner = partners.Current;
        var text = context.UserText ?? "";
        var started = time.GetUtcNow();
        var updater = new TaskUpdater(queue, context.TaskId, context.ContextId);
        var readers = PartnerPluginAccess.Current ?? await PartnerPluginAccess.CaptureAsync(partner, access, cancellationToken);
        var named = FirmReference(text) ?? FirmReference(HistoryText(context));
        var owner = context.IsContinuation ? await taskOwners.OwnerAsync(context.TaskId, cancellationToken) : null;
        var selected = readers.FirstOrDefault(reader => reader.Principal == owner)
            ?? BillingReaders(readers).FirstOrDefault(reader => named is null || reader.Principal.TenantId == named)
            ?? readers.FirstOrDefault();
        using var actorScope = selected is null ? null : PluginAccessContext.Use(selected.Principal, selected.Access);
        if (selected is not null) PartnerPluginAccess.SelectTask(context.TaskId, selected.Principal);
        var actor = selected?.Principal;

        try
        {
            if (WantsToStartARun(text) || context.IsContinuation)
            {
                await RunBillingAsync(readers, context, queue, updater, text, cancellationToken);
                return;
            }
            actor = await AnswerAsync(readers, queue, text, cancellationToken) ?? actor;
        }
        finally
        {
            await RecordAsync(partner, context.IsContinuation ? "continue" : "message", context.TaskId, started, cancellationToken,
                actor);
        }
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow();
        var owner = await taskOwners.OwnerAsync(context.TaskId, cancellationToken);
        await new TaskUpdater(queue, context.TaskId, context.ContextId).CancelAsync(cancellationToken);

        // A cancel can also come from the firm whose data is being worked on, who is not a partner at all. The
        // task ends the same way; who asked is recorded by whoever asked.
        if (Partner() is { } partner)
        {
            await RecordAsync(partner, "cancel", context.TaskId, started, cancellationToken, owner);
        }
    }

    private PartnerPrincipal? Partner()
    {
        try
        {
            return partners.Current;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A question that can be answered now is answered now — no task, as the protocol allows. A question about a
    /// named run is answered from the run's own record, because the entitlement check is the answer; anything else
    /// goes to the assistant itself, which is where the documentation and the reasoning live.
    /// </summary>
    private async Task<Principal?> AnswerAsync(IReadOnlyList<PartnerReader> readers, AgentEventQueue queue, string text, CancellationToken ct)
    {
        if (RunReference().Match(text) is { Success: true } match)
        {
            var (status, actor) = await StatusForAsync(readers, match.Groups["run"].Value, ct);
            await queue.EnqueueMessageAsync(
                new Message
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = MessageRole.Agent,
                    Parts = [new Part { Text = status }],
                }, ct);
            queue.Complete();
            return actor;
        }

        if (BillingReaders(readers).FirstOrDefault(reader => reader.Principal == PluginAccessContext.Principal) is not { } reader)
        {
            await queue.EnqueueMessageAsync(
                new Message
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Role = MessageRole.Agent,
                    Parts = [new Part { Text = OutOfScope }],
                }, ct);
            queue.Complete();
            return null;
        }

        // The assistant answers as a read-only principal for the first firm this partner may see; this plugin frames it.
        using var scope = PluginAccessContext.Use(reader.Principal, reader.Access);
        var answer = await assistant.AnswerAsync(reader.Principal, text, ct);
        await queue.EnqueueMessageAsync(Say(answer), ct);
        queue.Complete();
        return reader.Principal;
    }

    /// <summary>
    /// Not a user's token and not a user's entitlements: a read-only principal for each firm the partner may see, in its
    /// registration's order. The tenant comes from the partner's entitlement only, so no principal exists for a firm the
    /// partner was not registered for.
    /// </summary>
    private IEnumerable<PartnerReader> BillingReaders(IReadOnlyList<PartnerReader> readers) =>
        readers.Where(reader => AssistantAgentCard.HasBilling(installed, [reader]));

    private async Task<(string Text, Principal? Actor)> StatusForAsync(IReadOnlyList<PartnerReader> readers, string runId, CancellationToken ct)
    {
        // The entitlement decides, and it decides the same way whether or not the run exists: a partner learns
        // nothing about another firm, not even that one of its runs exists.
        foreach (var reader in BillingReaders(readers))
        {
            var status = await CallBillingAsync<BillingRunStatus>(reader, "get_billing_run_status",
                new Dictionary<string, object?> { ["runId"] = runId }, ct);
            if (status is not null)
            {
                return ($"Run {status.RunId} for {reader.Principal.TenantId.Value} is {status.Status} "
                    + $"({status.AccountCount} accounts, period {status.PeriodStart:yyyy-MM-dd} to {status.PeriodEnd:yyyy-MM-dd})"
                    + (status.FailureReason is null ? "." : $". Reason: {status.FailureReason}"), reader.Principal);
            }
        }
        return (OutOfScope, null);
    }

    /// <summary>The simulated run: submitted → working (with progress) → completed, or asked for what is missing.</summary>
    private async Task RunBillingAsync(IReadOnlyList<PartnerReader> readers, RequestContext context, AgentEventQueue queue,
        TaskUpdater updater, string text, CancellationToken ct)
    {
        var named = FirmReference(text) ?? FirmReference(HistoryText(context));
        var reader = BillingReaders(readers).FirstOrDefault(r => r.Principal == PluginAccessContext.Principal);
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

        // The firm the text names, or the partner's first; the run reads as that firm's reader, which exists only when the
        // partner may see it.
        if (reader is null || (named is not null && reader.Principal.TenantId != named)
            || (context.IsContinuation && FirmReference(HistoryText(context)) is { } original
            && reader.Principal.TenantId != original))
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
        // else. The cancel may reach either replica; the shared task store records it, and the run watches it there
        // (stop-anything).
        await using var watch = TaskCancelWatch.Start(tasks, context.TaskId, TimeSpan.FromMilliseconds(options.Value.CancelPollMs), time);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping, watch.Token);
        var work = run.Token;
        await updater.StartWorkAsync(Say($"Starting the billing run for {reader.Principal.TenantId.Value}, period {period}."), work);
        foreach (var step in new[] { "Loading accounts", "Applying fee schedules", "Producing invoices" })
        {
            work.ThrowIfCancellationRequested();
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(options.Value.SimulatedStepMs), time, work);
            await updater.StartWorkAsync(Say($"{step}…"), work);
        }

        // The artifact reports the firm's most recent seeded run: the lifecycle is what is being built here, not billing.
        var latest = (await CallBillingAsync<SearchBillingRunsResult>(reader, "search_billing_runs",
            new Dictionary<string, object?> { ["maxResults"] = 1 }, work))?.Runs.FirstOrDefault();
        await updater.AddArtifactAsync(
            [
                new Part
                {
                    // A DataPart: structured for the caller's code, not prose for a human to parse.
                    Data = System.Text.Json.JsonSerializer.SerializeToElement(new BillingRunArtifact
                    {
                        FirmId = reader.Principal.TenantId.Value,
                        Period = period,
                        RunId = latest?.RunId,
                        Status = latest?.Status ?? "unknown",
                        AccountCount = latest?.AccountCount,
                        Simulated = true,
                    }, Web),
                },
            ],
            name: "billing-run-result", cancellationToken: work);
        await updater.CompleteAsync(Say($"The simulated run for {reader.Principal.TenantId.Value} ({period}) is complete."), work);
    }

    private static string HistoryText(RequestContext context) =>
        string.Join(' ', context.Task?.History?.SelectMany(m => m.Parts ?? []).Select(p => p.Text).Where(t => t is not null) ?? []);

    private static Message Say(string text) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = MessageRole.Agent,
        Parts = [new Part { Text = text }],
    };

    /// <summary>
    /// One of billing's tools, called on billing's own server as the firm through the core's tool source, the way every
    /// other partner question reaches a domain. Null when billing is not installed, its server cannot answer, or the
    /// tool says it found nothing.
    /// </summary>
    private async Task<T?> CallBillingAsync<T>(PartnerReader reader, string tool, Dictionary<string, object?> arguments,
        CancellationToken ct) where T : class
    {
        // A read-only principal for one of the partner's firms: a partner reads one firm at a time, with no advisor
        // scope. Not the partner's own token, whose A2A audience the MCP server refuses by construction
        // (PartnerIdentity).
        using var scope = PluginAccessContext.Use(reader.Principal, reader.Access);
        var result = await tools.CallAsync(reader.Principal, BillingDomain, tool, arguments, ct);
        try
        {
            return result is { } content ? content.Deserialize<T>(Web) : null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning("a2a: billing's {Tool} answered in a shape this handler does not read ({Error})", tool, ex.GetType().Name);
            return null;
        }
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task RecordAsync(PartnerPrincipal partner, string operation, string taskId, DateTimeOffset started, CancellationToken ct,
        Principal? actor = null)
    {
        try
        {
            await audit.RecordAsync(
                new Principal(partner.PartnerId,
                    actor?.TenantId ?? (partner.AllowedFirms.Count > 0 ? partner.AllowedFirms.First() : TenantId.Firm("unknown")),
                    UserRole.READ_ONLY),
                RequestKind, $"a2a.{operation}", $"taskId={taskId}", "ok",
                (long)(time.GetUtcNow() - started).TotalMilliseconds, ct);
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

/// <summary>Billing's run status as this handler reads it from billing's tool (its own wire record, not billing's type).</summary>
internal sealed record BillingRunStatus(string RunId, string Status, DateOnly PeriodStart, DateOnly PeriodEnd, int AccountCount,
    string? FailureReason);

/// <summary>One of billing's runs as this handler reads it from billing's run search.</summary>
internal sealed record BillingRunSummary(string RunId, string Status, int AccountCount);

/// <summary>Billing's run search as this handler reads it.</summary>
internal sealed record SearchBillingRunsResult(IReadOnlyList<BillingRunSummary> Runs);

/// <summary>The simulated run's result as the partner's code reads it (a DataPart).</summary>
internal sealed class BillingRunArtifact
{
    public required string FirmId { get; init; }
    public required string Period { get; init; }
    public string? RunId { get; init; }
    public required string Status { get; init; }
    public int? AccountCount { get; init; }
    public bool Simulated { get; init; }
}

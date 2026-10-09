using Maf.Lab.Api.DataLifecycle;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Storage;

public sealed class MessageRetentionOptions
{
    public const string Section = "MessageRetention";

    /// <summary>
    /// How long message content is kept: the conversation, its messages and its turns. This is the compliance
    /// retention and is deliberately its own setting — the trace's (`Tracing:RetentionDays`) answers a different
    /// question, about how long the working of a turn is kept, and the two move independently.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(6);
}

/// <summary>
/// Removes message content once its retention has passed, wherever it is kept: a conversation, the messages the
/// model was given, and the turns they became. Idempotent, so every api replica can run it.
/// </summary>
public sealed class MessageRetentionService(
    IDbContextFactory<MafDbContext> db,
    CoreDataLifecycle lifecycle,
    IOptions<MessageRetentionOptions> options,
    TimeProvider time,
    ILogger<MessageRetentionService> logger) : BackgroundService
{
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().AddDays(-options.Value.RetentionDays);
        await using var ctx = await db.CreateDbContextAsync(ct);

        var owners = await ctx.Conversations.Where(c => c.LastActivityAt < cutoff.UtcDateTime)
            .Select(c => c.TenantId).Distinct().ToListAsync(ct);
        var removed = 0;
        var jobs = new LifecycleJobStore(db);
        var runner = new LifecycleJobRunner(jobs, time, TimeSpan.FromSeconds(1));
        foreach (var owner in owners)
        {
            var tenant = TenantId.Firm(owner);
            LifecycleJobSnapshot job;
            try
            {
                // Admission and hold creation share the database writer lock. This old core-only
                // schedule must obey that protocol too, before full per-tenant scheduling replaces it.
                job = await jobs.EnqueueAsync(new DataLifecycleScope(tenant), LifecycleOperation.Retention,
                    ["core"], cutoff, ct);
            }
            catch (LifecycleLegalHoldBlockedException blocked)
            {
                logger.LogInformation("message retention blocked by legal hold {HoldIds} for tenant {Tenant}",
                    string.Join(",", blocked.Holds.Select(h => h.Id)), owner);
                continue;
            }
            catch (LifecycleJobBusyException)
            {
                // Another replica or another lifecycle operation still owns this tenant.
                continue;
            }
            var count = 0;
            LifecycleJobSnapshot result;
            try
            {
                result = await runner.RunAsync(tenant, job.Id, async (snapshot, _, token) =>
                {
                    // Use the persisted cutoff and the same content closure as erasure, before turns
                    // lose the ownership of copied feedback/labels. The whole operation is awaited.
                    count = await lifecycle.PurgeAsync(new DataRetentionPolicy(snapshot.Tenant, snapshot.RetainFrom!.Value), token);
                }, ct);
            }
            catch
            {
                try
                {
                    // Admission already committed. A canceled request or failed claim must not
                    // strand a queued job. If it may be running, requesting stop keeps the slot
                    // until the attempt can prove that its work has unwound.
                    await jobs.RequestStopAsync(tenant, job.Id, CancellationToken.None);
                }
                catch (Exception cleanupFailure)
                {
                    logger.LogWarning("message retention stop request failed: {ErrorType}", cleanupFailure.GetType().Name);
                }
                throw;
            }
            ct.ThrowIfCancellationRequested();
            if (result.State == LifecycleJobState.Succeeded) removed += count;
            else if (result.State == LifecycleJobState.Failed)
                logger.LogWarning("message retention failed for tenant {Tenant}", owner);
        }
        return removed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, time);
        do
        {
            try
            {
                var removed = await PurgeAsync(stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("message retention removed {Count} conversation(s)", removed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("message retention failed: {ErrorType}", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

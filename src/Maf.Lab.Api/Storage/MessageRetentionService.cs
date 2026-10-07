using Maf.Lab.Api.Storage;
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
    IOptions<MessageRetentionOptions> options,
    TimeProvider time,
    ILogger<MessageRetentionService> logger) : BackgroundService
{
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        await using var ctx = await db.CreateDbContextAsync(ct);

        var stale = await ctx.Conversations.Where(c => c.LastActivityAt < cutoff).Select(c => c.Id).ToListAsync(ct);
        if (stale.Count == 0)
        {
            return 0;
        }
        // The turns go with the conversation, their core records with them. A plugin's diagnostics of them (the monitor's)
        // follow its own, shorter retention.
        await ctx.Turns.Where(t => stale.Contains(t.ConversationId)).ExecuteDeleteAsync(ct);
        await ctx.Messages.Where(m => stale.Contains(m.ConversationId)).ExecuteDeleteAsync(ct);
        await ctx.PendingWrites.Where(p => stale.Contains(p.ConversationId)).ExecuteDeleteAsync(ct);
        return await ctx.Conversations.Where(c => stale.Contains(c.Id)).ExecuteDeleteAsync(ct);
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

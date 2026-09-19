using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent.Tracing;

public sealed class TracingOptions
{
    public const string Section = "Tracing";
    public int RetentionDays { get; set; } = 7;
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>Deletes turn traces older than the retention period. Idempotent, so every api replica can run it.</summary>
public sealed class TraceRetentionService(IDbContextFactory<MafDbContext> db, IOptions<TracingOptions> options, TimeProvider time,
    ILogger<TraceRetentionService> logger) : BackgroundService
{
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        await using var ctx = await db.CreateDbContextAsync(ct);
        return await ctx.TurnTraces.Where(t => t.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, time);
        do
        {
            try
            {
                var deleted = await PurgeAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("trace retention removed {Count} trace(s)", deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("trace retention failed: {ErrorType}", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

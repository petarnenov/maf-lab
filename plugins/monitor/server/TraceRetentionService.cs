using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Monitor;

public sealed class TracingOptions
{
    public const string Section = "Tracing";
    public int RetentionDays { get; set; } = 7;
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Deletes the monitor's kept traces older than its retention period. Idempotent, so every api replica can run it. A
/// turn's core record is not here: it lives as long as its conversation.
/// </summary>
public sealed class TraceRetentionService(IDbContextFactory<DbContext> db, IOptions<TracingOptions> options, TimeProvider time,
    ILogger<TraceRetentionService> logger) : BackgroundService
{
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        await using var ctx = await db.CreateDbContextAsync(ct);
        return await ctx.Set<TurnDiagnosticsRow>().Where(t => t.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
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

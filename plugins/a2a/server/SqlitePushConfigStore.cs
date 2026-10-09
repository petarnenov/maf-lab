using Maf.Lab.A2A;
using Microsoft.EntityFrameworkCore;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Plugins.A2A;

/// <summary>
/// The webhooks partners registered, in the database the replicas share — so a caller can register on one replica
/// and be notified by whichever one runs its task.
/// </summary>
public sealed class SqlitePushConfigStore(IDbContextFactory<DbContext> db, TimeProvider time, IInstalledPlugins installed) : IPushConfigStore
{
    public async Task SaveAsync(PushConfigRecord config, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await EnsureVisible(ctx, config.TaskId, ct);
        var existing = await ctx.Set<A2APushConfigRow>().FirstOrDefaultAsync(c => c.Id == config.Id && c.TaskId == config.TaskId, ct);
        if (existing is null)
        {
            ctx.Set<A2APushConfigRow>().Add(new A2APushConfigRow
            {
                Id = config.Id,
                TaskId = config.TaskId,
                Url = config.Url,
                Token = config.Token,
                CreatedAt = time.GetUtcNow().UtcDateTime,
            });
        }
        else
        {
            existing.Url = config.Url;
            existing.Token = config.Token;
        }
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PushConfigRecord>> ListAsync(string taskId, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await EnsureVisible(ctx, taskId, ct);
        var rows = await ctx.Set<A2APushConfigRow>().AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        return [.. rows.Select(r => new PushConfigRecord(r.Id, r.TaskId, r.Url, r.Token))];
    }

    public async Task DeleteAsync(string taskId, string id, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await EnsureVisible(ctx, taskId, ct);
        await ctx.Set<A2APushConfigRow>().Where(c => c.TaskId == taskId && c.Id == id).ExecuteDeleteAsync(ct);
    }

    private async Task EnsureVisible(DbContext ctx, string taskId, CancellationToken ct)
    {
        if (PartnerPluginAccess.Current is not null
            && !await PartnerPluginAccess.VisibleTasks(ctx.Set<A2ATaskRow>(), installed).AnyAsync(task => task.Id == taskId, ct))
            throw new global::A2A.A2AException("Task not found.", global::A2A.A2AErrorCode.TaskNotFound);
    }
}

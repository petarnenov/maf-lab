using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.A2A;

/// <summary>Removal reads and stops live tasks through their owning server and shared store.</summary>
internal sealed class A2AOpenWork(IDbContextFactory<DbContext> db, global::A2A.A2AServer server) : IOpenWork
{
    internal static readonly string[] Running = ["Submitted", "Working", "InputRequired", "AuthRequired"];

    public async Task<IReadOnlyList<OpenWorkItem>> ListOpenAsync(CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        return await context.Set<A2ATaskRow>().AsNoTracking().Where(t => Running.Contains(t.State))
            .Select(t => new OpenWorkItem("task", t.Id, t.State)).ToListAsync(ct);
    }

    public async Task CancelAllAsync(CancellationToken ct)
    {
        foreach (var task in await ListOpenAsync(ct))
        {
            await server.CancelTaskAsync(new global::A2A.CancelTaskRequest { Id = task.Id }, ct);
        }
    }
}

using Maf.Lab.A2A;
using System.Text.Json;
using A2A;
using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.A2A;

/// <summary>
/// Task state in the database the replicas already share. The SDK ships only <see cref="InMemoryTaskStore"/>, which
/// two replicas behind a balancer cannot share: a task started on one would be invisible on the other, and a
/// caller that reconnected to the wrong replica would be told its task does not exist.
///
/// Every save also tells the dispatcher a state change happened, because the store is the one place every
/// transition passes through.
/// </summary>
public sealed class SqliteTaskStore(
    IDbContextFactory<MafDbContext> db,
    TimeProvider time,
    PushNotificationDispatcher push,
    Maf.Lab.A2A.IPartnerAccessor partners)
    : ITaskStore
{
    private static readonly JsonSerializerOptions Json = A2AJsonUtilities.DefaultOptions;

    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        var row = await ctx.A2ATasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        return row is null ? null : JsonSerializer.Deserialize<AgentTask>(row.Json, Json);
    }

    public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        var state = task.Status?.State.ToString() ?? "";
        await using (var ctx = await db.CreateDbContextAsync(cancellationToken))
        {
            var row = await ctx.A2ATasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
            var changed = row is null || row.State != state;
            if (row is null)
            {
                ctx.A2ATasks.Add(new A2ATaskRow
                {
                    Id = taskId,
                    ContextId = task.ContextId ?? "",
                    // Who this task belongs to is known at the moment it is created: the request that created it
                    // was authenticated, and its entitlement is what decides who may see or stop the task later.
                    PartnerId = Metadata(task, "partnerId") ?? Caller()?.PartnerId,
                    FirmId = Caller() is { AllowedFirms.Count: > 0 } caller ? caller.AllowedFirms.First().Value : null,
                    State = state,
                    Json = JsonSerializer.Serialize(task, Json),
                    CreatedAt = time.GetUtcNow().UtcDateTime,
                    UpdatedAt = time.GetUtcNow().UtcDateTime,
                });
            }
            else
            {
                row.State = state;
                row.Json = JsonSerializer.Serialize(task, Json);
                row.UpdatedAt = time.GetUtcNow().UtcDateTime;
            }
            await ctx.SaveChangesAsync(cancellationToken);
            if (!changed)
            {
                return;
            }
        }
        // Outside the write: a slow or failing webhook must never hold the task's own transaction.
        await push.OnStateChangedAsync(taskId, task, cancellationToken);
    }

    public async Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        await ctx.A2ATasks.Where(t => t.Id == taskId).ExecuteDeleteAsync(cancellationToken);
        await ctx.A2APushConfigs.Where(c => c.TaskId == taskId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        var query = ctx.A2ATasks.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.ContextId))
        {
            query = query.Where(t => t.ContextId == request.ContextId);
        }
        if (request.Status is { } status)
        {
            var wanted = status.ToString();
            query = query.Where(t => t.State == wanted);
        }
        var size = request.PageSize is > 0 and <= 200 ? request.PageSize.Value : 50;
        var rows = await query.OrderByDescending(t => t.UpdatedAt).Take(size).ToListAsync(cancellationToken);
        return new ListTasksResponse
        {
            Tasks = [.. rows.Select(r => JsonSerializer.Deserialize<AgentTask>(r.Json, Json)!)],
            PageSize = size,
            TotalSize = rows.Count,
        };
    }

    /// <summary>The partner whose request is being served, when there is one.</summary>
    private Maf.Lab.A2A.PartnerPrincipal? Caller()
    {
        try
        {
            return partners.Current;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            // Saved outside a partner's request — background work continuing after the response, or a test.
            return null;
        }
    }

    private static string? Metadata(AgentTask task, string key) =>
        task.Metadata is not null && task.Metadata.TryGetValue(key, out var value) ? value.ToString() : null;
}

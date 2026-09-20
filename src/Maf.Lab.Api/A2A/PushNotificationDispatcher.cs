using A2A;
using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.A2A;

/// <summary>
/// Delivers one POST per task state change to whatever webhook the caller registered.
///
/// The SDK declares push-notification configuration on its request handler but every one of those methods throws in
/// 1.0.0-preview2, so both the storage and the delivery are ours (see DECISIONS.md). A failing webhook is retried a
/// fixed number of times and then recorded — it never fails the task, because the caller's receiver being down is
/// not the task's problem.
/// </summary>
public sealed class PushNotificationDispatcher(
    IDbContextFactory<MafDbContext> db,
    IHttpClientFactory http,
    IOptions<A2AOptions> options,
    TimeProvider time,
    ILogger<PushNotificationDispatcher> logger)
{
    public async Task OnStateChangedAsync(string taskId, AgentTask task, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var configs = await ctx.A2APushConfigs.AsNoTracking().Where(c => c.TaskId == taskId).ToListAsync(ct);
        if (configs.Count == 0)
        {
            return;
        }
        var state = task.Status?.State.ToString() ?? "";
        foreach (var config in configs)
        {
            var (delivered, attempts, error) = await DeliverAsync(config, taskId, task, ct);
            ctx.A2APushDeliveries.Add(new A2APushDeliveryRow
            {
                TaskId = taskId,
                State = state,
                Url = config.Url,
                At = time.GetUtcNow().UtcDateTime,
                Attempts = attempts,
                Delivered = delivered,
                Error = error,
            });
        }
        await ctx.SaveChangesAsync(ct);
    }

    private async Task<(bool Delivered, int Attempts, string? Error)> DeliverAsync(
        A2APushConfigRow config, string taskId, AgentTask task, CancellationToken ct)
    {
        var client = http.CreateClient("a2a-push");
        var attempts = 0;
        string? error = null;
        for (var attempt = 0; attempt <= Math.Max(0, options.Value.PushRetries); attempt++)
        {
            attempts++;
            try
            {
                // The task as the 1.0 specification describes it — the same shape the caller would have been sent
                // over the wire — so the receiver can act on it without calling back. Serialized up front, so the
                // delivery carries a Content-Length: a webhook that reads by length would otherwise see nothing.
                var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(SpecWire.ResponseToSpec(
                    System.Text.Json.JsonSerializer.SerializeToNode(task, A2AJsonUtilities.DefaultOptions)));
                using var request = new HttpRequestMessage(HttpMethod.Post, config.Url)
                {
                    Content = new ByteArrayContent(payload)
                    {
                        Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") },
                    },
                };
                if (!string.IsNullOrWhiteSpace(config.Token))
                {
                    // The caller's own token, echoed back so the receiver can tell the call is genuine.
                    request.Headers.Add("X-A2A-Notification-Token", config.Token);
                }
                using var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    return (true, attempts, null);
                }
                error = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The message, not just the type: "connection refused" and "name not resolved" are different
                // problems for whoever registered the webhook.
                error = $"{ex.GetType().Name}: {ex.Message}{(ex.InnerException is { } inner ? $" ({inner.GetType().Name}: {inner.Message})" : "")}";
                error = error.Length > 200 ? error[..200] : error;
            }
        }
        logger.LogWarning("push delivery failed task={TaskId} attempts={Attempts} error={Error}", taskId, attempts, error);
        return (false, attempts, error);
    }
}

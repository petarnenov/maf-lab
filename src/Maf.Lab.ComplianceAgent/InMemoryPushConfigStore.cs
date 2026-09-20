using System.Collections.Concurrent;
using Maf.Lab.A2A;

namespace Maf.Lab.ComplianceAgent;

/// <summary>
/// The reviewer's webhooks, for as long as the review lasts. It keeps no database because it keeps no state worth
/// outliving a process: a review is minutes of work and nobody reads it afterwards. The limitation — a replica's
/// reviews die with it — is recorded in DECISIONS.md and is what a caller's deadline is for.
/// </summary>
public sealed class InMemoryPushConfigStore : IPushConfigStore
{
    private readonly ConcurrentDictionary<string, List<PushConfigRecord>> byTask = new(StringComparer.Ordinal);

    public Task SaveAsync(PushConfigRecord config, CancellationToken ct)
    {
        var configs = byTask.GetOrAdd(config.TaskId, _ => []);
        lock (configs)
        {
            configs.RemoveAll(c => c.Id == config.Id);
            configs.Add(config);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PushConfigRecord>> ListAsync(string taskId, CancellationToken ct)
    {
        if (!byTask.TryGetValue(taskId, out var configs))
        {
            return Task.FromResult<IReadOnlyList<PushConfigRecord>>([]);
        }
        lock (configs)
        {
            return Task.FromResult<IReadOnlyList<PushConfigRecord>>([.. configs]);
        }
    }

    public Task DeleteAsync(string taskId, string id, CancellationToken ct)
    {
        if (byTask.TryGetValue(taskId, out var configs))
        {
            lock (configs)
            {
                configs.RemoveAll(c => c.Id == id);
            }
        }
        return Task.CompletedTask;
    }
}

using System.Collections.Concurrent;
using Maf.Lab.Domain.SharedState;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.TestSupport;

/// <summary>
/// The shared stores, in memory, for tests that are about a service rather than about the store. A service still
/// refuses to start without a store; what these prove is that it does not care which one.
/// </summary>
public sealed class FakeRunStateStore : IRunStateStore
{
    private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);

    public Task SaveAsync(RunState state, CancellationToken ct)
    {
        _runs[state.RunId] = state;
        return Task.CompletedTask;
    }

    public Task<RunState?> GetAsync(string runId, CancellationToken ct) =>
        Task.FromResult(_runs.TryGetValue(runId, out var state) ? state : null);

    /// <summary>Forgets a run, as the store does once its grace period has passed.</summary>
    public void Forget(string runId) => _runs.TryRemove(runId, out _);
}

public sealed class FakeIdempotencyStore(TimeProvider? time = null) : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotentAnswer> _answers = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<(IdempotencyOutcome Outcome, IdempotentAnswer? Answer)> CheckAsync(
        string firmId, string key, string requestDigest, CancellationToken ct)
    {
        if (!_answers.TryGetValue($"{firmId}:{key}", out var recorded))
        {
            return Task.FromResult((IdempotencyOutcome.Fresh, (IdempotentAnswer?)null));
        }
        return Task.FromResult(recorded.RequestDigest == requestDigest
            ? (IdempotencyOutcome.Replay, (IdempotentAnswer?)recorded)
            : (IdempotencyOutcome.Conflict, (IdempotentAnswer?)recorded));
    }

    public Task RecordAsync(string firmId, string key, string requestDigest, string answer, CancellationToken ct)
    {
        _answers[$"{firmId}:{key}"] = new IdempotentAnswer(key, requestDigest, answer, _time.GetUtcNow());
        return Task.CompletedTask;
    }
}

/// <summary>
/// Puts the in-memory stores into a test host. A service refuses to start without a store; what a test that is
/// not about the store needs is simply that one is there.
/// </summary>
public static class SharedStoreTestHost
{
    public static IWebHostBuilder WithFakeSharedState(this IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRunStateStore>();
            services.AddSingleton<IRunStateStore>(new FakeRunStateStore());
            services.RemoveAll<IIdempotencyStore>();
            services.AddSingleton<IIdempotencyStore>(new FakeIdempotencyStore());
        });
}

/// <summary>The reviewer's webhooks in memory, for tests that are about the reviewer and not about the store.</summary>
public sealed class FakePushConfigStore : Maf.Lab.A2A.IPushConfigStore
{
    private readonly ConcurrentDictionary<string, List<Maf.Lab.A2A.PushConfigRecord>> _byTask = new(StringComparer.Ordinal);

    public Task SaveAsync(Maf.Lab.A2A.PushConfigRecord config, CancellationToken ct)
    {
        var configs = _byTask.GetOrAdd(config.TaskId, _ => []);
        lock (configs)
        {
            configs.RemoveAll(c => c.Id == config.Id);
            configs.Add(config);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Maf.Lab.A2A.PushConfigRecord>> ListAsync(string taskId, CancellationToken ct)
    {
        if (!_byTask.TryGetValue(taskId, out var configs))
        {
            return Task.FromResult<IReadOnlyList<Maf.Lab.A2A.PushConfigRecord>>([]);
        }
        lock (configs)
        {
            return Task.FromResult<IReadOnlyList<Maf.Lab.A2A.PushConfigRecord>>([.. configs]);
        }
    }

    public Task DeleteAsync(string taskId, string id, CancellationToken ct)
    {
        if (_byTask.TryGetValue(taskId, out var configs))
        {
            lock (configs)
            {
                configs.RemoveAll(c => c.Id == id);
            }
        }
        return Task.CompletedTask;
    }
}

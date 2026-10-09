using System.Collections.Concurrent;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Plugins;

/// <summary>Cache-aside entitlement reads, invalidated by Redis and bounded by a monotonic 30-second TTL.</summary>
public sealed class PluginAccess : IPluginAccess, IDisposable
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly IPluginEntitlements _store;
    private readonly PluginCatalogue _installed;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<TenantId, TenantCache> _cache = new();
    private readonly IDisposable _subscription;

    public PluginAccess(IPluginEntitlements store, PluginCatalogue installed, IPluginAccessChanges changes, TimeProvider time)
    {
        _store = store;
        _installed = installed;
        _time = time;
        _subscription = changes.Subscribe(key =>
        {
            if (_cache.TryGetValue(key, out var state))
            {
                Interlocked.Increment(ref state.Generation);
                Volatile.Write(ref state.Current, null);
            }
        });
    }

    private sealed class TenantCache
    {
        public readonly SemaphoreSlim ReadGate = new(1, 1);
        public long Generation;
        public Entry? Current;
    }
    private sealed record Entry(long Generation, long ReadStarted, IReadOnlyList<PluginEntitlement> Rows);

    public async Task<PluginAccessSnapshot> For(Principal principal, CancellationToken ct)
    {
        if (principal.TenantId.IsShared || string.IsNullOrWhiteSpace(principal.TenantId.Value)) return PluginAccessSnapshot.Empty;
        var state = _cache.GetOrAdd(principal.TenantId, _ => new());
        IReadOnlyList<PluginEntitlement> rows;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var generation = Volatile.Read(ref state.Generation);
            var entry = Volatile.Read(ref state.Current);
            if (entry is not null && entry.Generation == generation && _time.GetElapsedTime(entry.ReadStarted) < CacheTtl)
            {
                rows = entry.Rows;
                break;
            }
            await state.ReadGate.WaitAsync(ct);
            try
            {
                generation = Volatile.Read(ref state.Generation);
                entry = Volatile.Read(ref state.Current);
                if (entry is not null && entry.Generation == generation && _time.GetElapsedTime(entry.ReadStarted) < CacheTtl) continue;
                var started = _time.GetTimestamp();
                var read = await _store.ReadAsync(principal, ct);
                if (generation != Volatile.Read(ref state.Generation) || _time.GetElapsedTime(started) >= CacheTtl) continue;
                entry = new(generation, started, read);
                Volatile.Write(ref state.Current, entry);
                if (generation != Volatile.Read(ref state.Generation)) continue;
                rows = read;
                break;
            }
            finally { state.ReadGate.Release(); }
        }
        var enabled = rows.Where(r => r.Allowed && r.Enabled).Select(r => r.Plugin).ToHashSet(StringComparer.Ordinal);
        return new(_installed.Current.Plugins.Where(p =>
            (p.Manifest.PrivateTo is null || p.Manifest.PrivateTo == principal.TenantId.Value)
            && (p.Manifest.Scope == PluginScopes.Installation || enabled.Contains(p.Name))).Select(p => p.Name));
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}

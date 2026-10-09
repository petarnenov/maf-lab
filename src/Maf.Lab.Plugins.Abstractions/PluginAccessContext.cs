using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>Immutable entitlement snapshot for this async request/turn, like Activity.Current.</summary>
public static class PluginAccessContext
{
    private sealed record Scope(Principal Principal, PluginAccessSnapshot Snapshot);
    private static readonly AsyncLocal<Scope?> Ambient = new();
    public static Principal? Principal => Ambient.Value?.Principal;
    public static PluginAccessSnapshot? Current => Ambient.Value?.Snapshot;
    public static PluginAccessSnapshot? For(Principal principal) => Ambient.Value is { } scope && scope.Principal == principal ? scope.Snapshot : null;
    public static IDisposable Use(Principal principal, PluginAccessSnapshot snapshot)
    {
        var previous = Ambient.Value;
        Ambient.Value = new(principal, snapshot);
        return new Restore(() => Ambient.Value = previous);
    }
    private sealed class Restore(Action action) : IDisposable
    {
        private Action? _restore = action;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}

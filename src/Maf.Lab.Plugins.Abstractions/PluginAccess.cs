using System.Collections.Frozen;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>The caller's installed, allowed and enabled plugins, frozen for a request or turn.</summary>
public sealed class PluginAccessSnapshot(IEnumerable<string> names)
{
    private readonly FrozenSet<string> _names = names.ToFrozenSet(StringComparer.Ordinal);
    public IReadOnlySet<string> Names => _names;
    public bool IsInUse(string plugin) => _names.Contains(plugin);
    public static PluginAccessSnapshot Empty { get; } = new([]);
}

/// <summary>One source of entitlement decisions; the principal comes from the validated token.</summary>
public interface IPluginAccess
{
    Task<PluginAccessSnapshot> For(Principal principal, CancellationToken ct);
}

public sealed record PluginEntitlement(string Plugin, bool Allowed, bool Enabled);
public enum PluginChangeStatus { Changed, Unchanged, NotInstalled, NotTenantScoped, PrivateToAnotherTenant, NotAllowed, Forbidden }
public sealed record PluginChange(PluginChangeStatus Status, PluginEntitlement? Entitlement = null);

/// <summary>Tenant switches and operator allowances; no caller can supply a different tenant.</summary>
public interface IPluginEntitlements
{
    Task<IReadOnlyList<PluginEntitlement>> ReadAsync(Principal principal, CancellationToken ct);
    Task<PluginChange> AllowAsync(Principal principal, string plugin, bool allowed, bool enable, CancellationToken ct);
    Task<PluginChange> EnableAsync(Principal principal, string plugin, bool enabled, CancellationToken ct);
}

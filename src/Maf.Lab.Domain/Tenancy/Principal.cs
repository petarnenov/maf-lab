namespace Maf.Lab.Domain.Tenancy;

/// <summary>
/// The authenticated caller. Built only from a validated token. It carries only what the core needs: who, which tenant,
/// and a core role. Domain roles and attributes (a billing advisor's ids) stay in the token as claims of their own and
/// are read only by the domain's server (rename-firm-to-tenant).
/// </summary>
public sealed record Principal(string UserId, TenantId TenantId, Role Role)
{
    /// <summary>Opaque group identifiers from the validated identity, immutable for the request/turn.</summary>
    private System.Collections.Frozen.FrozenSet<string> _groupIds = System.Collections.Frozen.FrozenSet<string>.Empty;
    public IReadOnlySet<string> GroupIds
    {
        get => _groupIds;
        init => _groupIds = System.Collections.Frozen.FrozenSet.ToFrozenSet(value, StringComparer.Ordinal);
    }

    public bool Equals(Principal? other) => other is not null && UserId == other.UserId
        && TenantId == other.TenantId && Role == other.Role && GroupIds.SetEquals(other.GroupIds);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(UserId); hash.Add(TenantId); hash.Add(Role);
        foreach (var group in GroupIds.Order(StringComparer.Ordinal)) hash.Add(group, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
    /// <summary>Tenants whose content this principal may read: its own tenant and the shared corpus.</summary>
    public IReadOnlyList<TenantId> ReadableTenants => [TenantId, TenantId.Shared];

    public bool IsPlatformAdmin => Role == Role.PLATFORM_ADMIN;

    public bool IsTenantAdmin => Role == Role.TENANT_ADMIN;
}

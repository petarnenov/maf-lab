namespace Maf.Lab.Domain.Tenancy;

/// <summary>
/// The authenticated caller. Built only from a validated token. It carries only what the core needs: who, which tenant,
/// and a core role. Domain roles and attributes (a billing advisor's ids) stay in the token as claims of their own and
/// are read only by the domain's server (rename-firm-to-tenant).
/// </summary>
public sealed record Principal(string UserId, TenantId TenantId, Role Role)
{
    /// <summary>Tenants whose content this principal may read: its own tenant and the shared corpus.</summary>
    public IReadOnlyList<TenantId> ReadableTenants => [TenantId, TenantId.Shared];

    public bool IsTenantAdmin => Role == Role.TENANT_ADMIN;
}

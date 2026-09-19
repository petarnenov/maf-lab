namespace Maf.Lab.Domain.Tenancy;

/// <summary>The authenticated caller. Built only from a validated token.</summary>
public sealed record Principal(string UserId, TenantId FirmId, Role Role, IReadOnlyList<string> AllowedAdvisorIds)
{
    /// <summary>Tenants whose content this principal may read: its own firm and the shared corpus.</summary>
    public IReadOnlyList<TenantId> ReadableTenants => [FirmId, TenantId.Shared];

    public bool IsFirmAdmin => Role == Role.FIRM_ADMIN;
}

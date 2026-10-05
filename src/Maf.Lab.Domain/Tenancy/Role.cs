namespace Maf.Lab.Domain.Tenancy;

/// <summary>
/// The core's roles, which any tenant has. Domain roles (billing's advisor or ops) are claims the core never reads
/// (<see cref="PrincipalClaims.DomainRoles"/>).
/// </summary>
public enum Role
{
    TENANT_ADMIN,
    USER,
    READ_ONLY,
}

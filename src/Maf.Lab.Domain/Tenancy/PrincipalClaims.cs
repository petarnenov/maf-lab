using System.Security.Claims;
using System.Collections.Frozen;

namespace Maf.Lab.Domain.Tenancy;

/// <summary>Claim names used by the dev issuer, and the one mapping from claims to <see cref="Principal"/>.</summary>
public static class PrincipalClaims
{
    public const string UserId = "sub";
    public const string TenantId = "tenant_id";
    public const string Role = "role";
    public const string Groups = "groups";

    /// <summary>Domain roles, such as <c>billing:advisor</c>. The core never reads them; the domain's own server does.</summary>
    public const string DomainRoles = "domain_roles";

    /// <summary>A billing advisor's ids: a domain attribute, read only by the billing server.</summary>
    public const string AdvisorIds = "advisor_ids";

    /// <summary>The tenant claim's name before rename-firm-to-tenant, accepted for one release.</summary>
    public const string LegacyFirmId = "firm_id";

    /// <summary>Roles named before rename-firm-to-tenant, accepted for one release and mapped to the core roles.</summary>
    private static readonly IReadOnlyDictionary<string, Tenancy.Role> LegacyRoles = new Dictionary<string, Tenancy.Role>(StringComparer.Ordinal)
    {
        ["FIRM_ADMIN"] = Tenancy.Role.TENANT_ADMIN,
        ["ADVISOR"] = Tenancy.Role.USER,
        ["OPS"] = Tenancy.Role.USER,
    };

    public static bool TryCreate(ClaimsPrincipal? user, out Principal principal)
    {
        principal = null!;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var sub = user.FindFirst(UserId)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantValue = user.FindFirst(TenantId)?.Value ?? user.FindFirst(LegacyFirmId)?.Value;
        var role = user.FindFirst(Role)?.Value ?? user.FindFirst(ClaimTypes.Role)?.Value;

        if (string.IsNullOrWhiteSpace(sub)
            || !Tenancy.TenantId.TryParse(tenantValue, out var tenant)
            || tenant.IsShared
            || !TryParseRole(role, out var parsedRole))
        {
            return false;
        }

        principal = new Principal(sub, tenant, parsedRole)
        {
            GroupIds = user.FindAll(Groups).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToFrozenSet(StringComparer.Ordinal),
        };
        return true;
    }

    /// <summary>A core role, or a pre-rename role name mapped to one.</summary>
    public static bool TryParseRole(string? value, out Tenancy.Role role)
    {
        role = default;
        if (value is null)
        {
            return false;
        }
        if (Enum.GetNames<Tenancy.Role>().Contains(value, StringComparer.Ordinal))
        {
            role = Enum.Parse<Tenancy.Role>(value);
            return true;
        }
        return LegacyRoles.TryGetValue(value, out role);
    }
}

using System.Security.Claims;

namespace Maf.Lab.Domain.Tenancy;

/// <summary>Claim names used by the dev issuer, and the one mapping from claims to <see cref="Principal"/>.</summary>
public static class PrincipalClaims
{
    public const string UserId = "sub";
    public const string FirmId = "firm_id";
    public const string Role = "role";
    public const string AdvisorId = "advisor_id";

    public static bool TryCreate(ClaimsPrincipal? user, out Principal principal)
    {
        principal = null!;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var sub = user.FindFirst(UserId)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var firm = user.FindFirst(FirmId)?.Value;
        var role = user.FindFirst(Role)?.Value ?? user.FindFirst(ClaimTypes.Role)?.Value;

        if (string.IsNullOrWhiteSpace(sub)
            || !TenantId.TryParse(firm, out var tenant)
            || tenant.IsShared
            || !Enum.TryParse<Role>(role, ignoreCase: false, out var parsedRole))
        {
            return false;
        }

        var advisors = user.FindAll(AdvisorId).Select(c => c.Value).Distinct().ToArray();
        principal = new Principal(sub, tenant, parsedRole, advisors);
        return true;
    }
}

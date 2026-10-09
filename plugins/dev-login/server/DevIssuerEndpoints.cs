using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.DevLogin;

/// <summary>Development-only issuer, mapped only by the installed dev/qa plugin.</summary>
public static class DevIssuerEndpoints
{
    /// <summary>
    /// A dev persona. <see cref="Role"/> is a core role; <see cref="DomainRoles"/> and <see cref="AdvisorIds"/> are billing
    /// domain claims the core never reads (rename-firm-to-tenant).
    /// </summary>
    public sealed record DevUser(string UserId, string TenantId, string Role, IReadOnlyList<string> DomainRoles,
        IReadOnlyList<string> AdvisorIds, string Label);

    /// <param name="FirmId">The tenant's pre-rename name, accepted for one release when <paramref name="TenantId"/> is absent.</param>
    public sealed record TokenRequest(string? UserId = null, string? TenantId = null, string? Role = null, IReadOnlyList<string>? DomainRoles = null,
        IReadOnlyList<string>? AdvisorIds = null, string? FirmId = null, string? Audience = null, string? Persona = null);
    public sealed record TokenResponse(string Token, DateTimeOffset ExpiresAt);

    private const string Advisor = "billing:advisor";
    private const string Ops = "billing:ops";

    public static readonly IReadOnlyList<DevUser> Personas =
    [
        new("operator", "firm-a", nameof(Role.PLATFORM_ADMIN), [], [], "Platform operator"),
        new("alice", "firm-a", nameof(Role.TENANT_ADMIN), [], [], "Alice — Acme Wealth Partners, tenant admin"),
        new("adam", "firm-a", nameof(Role.USER), [Advisor], ["adv-a-1", "adv-a-2"], "Adam — Acme Wealth Partners, advisor"),
        new("olga", "firm-a", nameof(Role.USER), [Ops], [], "Olga — Acme Wealth Partners, billing ops"),
        new("rita", "firm-a", nameof(Role.READ_ONLY), [], [], "Rita — Acme Wealth Partners, read-only"),
        new("bob", "firm-b", nameof(Role.TENANT_ADMIN), [], [], "Bob — Northwind Capital, tenant admin"),
        new("bianca", "firm-b", nameof(Role.USER), [Advisor], ["adv-b-1"], "Bianca — Northwind Capital, advisor"),
        new("carol", "firm-c", nameof(Role.TENANT_ADMIN), [], [], "Carol — Contoso Advisors, tenant admin"),
        new("chris", "firm-c", nameof(Role.USER), [Advisor], ["adv-c-1"], "Chris — Contoso Advisors, advisor"),
    ];

    public static IEndpointRouteBuilder MapDevIssuer(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev").AllowAnonymous();
        dev.MapGet("/users", () => Personas);
        dev.MapPost("/token", async Task<IResult> (TokenRequest request, IOptions<AuthOptions> auth,
            IPluginAccess access, IInstalledPlugins installed, TimeProvider clock, CancellationToken ct) =>
        {
            var name = request.Persona ?? request.UserId;
            var persona = Personas.FirstOrDefault(p => p.UserId == name);
            var target = request.TenantId ?? request.FirmId ?? persona?.TenantId;
            var requestedRole = string.IsNullOrWhiteSpace(request.Role) ? persona?.Role : request.Role;
            if (string.IsNullOrWhiteSpace(name) || !TenantId.TryParse(target, out var tenant) || tenant.IsShared
                || !PrincipalClaims.TryParseRole(requestedRole, out var role))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["A persona/user, organization and core role are required."] });
            // The operator is the explicit dev stand-in for an organization-scoped operator token.
            if (persona is not null && (persona.UserId != "operator" && persona.TenantId != tenant.Value || persona.Role != role.ToString()))
                return Results.Forbid();
            var audience = request.Audience ?? auth.Value.Audience;
            if (audience != auth.Value.Audience)
            {
                var manifest = installed.Installed().FirstOrDefault(m => m.Name == audience);
                if (manifest is null) return Results.NotFound();
                if (!(await access.For(new Principal(name, tenant, role), ct)).IsInUse(audience)) return Results.Forbid();
            }
            var (token, expires) = DevJwt.Issue(auth.Value, name, tenant, role, clock.GetUtcNow(),
                domainRoles: request.DomainRoles ?? persona?.DomainRoles, advisorIds: request.AdvisorIds ?? persona?.AdvisorIds,
                audience: audience);
            return Results.Ok(new TokenResponse(token, expires));
        });
        return app;
    }
}

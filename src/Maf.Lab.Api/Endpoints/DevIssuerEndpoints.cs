using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Endpoints;

/// <summary>Local development token issuer. Disabled unless Auth:EnableDevIssuer is true.</summary>
public static class DevIssuerEndpoints
{
    /// <summary>
    /// A dev persona. <see cref="Role"/> is a core role; <see cref="DomainRoles"/> and <see cref="AdvisorIds"/> are billing
    /// domain claims the core never reads (rename-firm-to-tenant).
    /// </summary>
    public sealed record DevUser(string UserId, string TenantId, string Role, IReadOnlyList<string> DomainRoles,
        IReadOnlyList<string> AdvisorIds, string Label);

    /// <param name="FirmId">The tenant's pre-rename name, accepted for one release when <paramref name="TenantId"/> is absent.</param>
    public sealed record TokenRequest(string UserId, string? TenantId, string Role, IReadOnlyList<string>? DomainRoles,
        IReadOnlyList<string>? AdvisorIds, string? FirmId = null);
    public sealed record TokenResponse(string Token, DateTimeOffset ExpiresAt);

    private const string Advisor = "billing:advisor";
    private const string Ops = "billing:ops";

    public static readonly IReadOnlyList<DevUser> Personas =
    [
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
        dev.MapPost("/token", (TokenRequest request, IOptions<AuthOptions> auth) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId)
                || !TenantId.TryParse(request.TenantId ?? request.FirmId, out var tenant) || tenant.IsShared
                || !PrincipalClaims.TryParseRole(request.Role, out var role))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["userId, a tenant id (firm-*) and a role (TENANT_ADMIN, USER, READ_ONLY) are required."],
                });
            }
            // A persona's domain claims come from the persona itself when the request names none, so a client that only
            // knows the core shape (user, tenant, role) still gets the billing claims the domain's server reads.
            var persona = Personas.FirstOrDefault(p => p.UserId == request.UserId && p.TenantId == tenant.Value);
            var (token, expires) = DevJwt.Issue(auth.Value, request.UserId, tenant, role,
                domainRoles: request.DomainRoles ?? persona?.DomainRoles, advisorIds: request.AdvisorIds ?? persona?.AdvisorIds);
            return Results.Ok(new TokenResponse(token, expires));
        });
        return app;
    }
}

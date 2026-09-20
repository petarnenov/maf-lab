using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Endpoints;

/// <summary>Local development token issuer. Disabled unless Auth:EnableDevIssuer is true.</summary>
public static class DevIssuerEndpoints
{
    public sealed record DevUser(string UserId, string FirmId, string Role, IReadOnlyList<string> AdvisorIds, string Label);
    public sealed record TokenRequest(string UserId, string FirmId, string Role, IReadOnlyList<string>? AdvisorIds);
    public sealed record TokenResponse(string Token, DateTimeOffset ExpiresAt);

    public static readonly IReadOnlyList<DevUser> Personas =
    [
        new("alice", "firm-a", nameof(Role.FIRM_ADMIN), [], "Alice — Acme Wealth Partners, firm admin"),
        new("adam", "firm-a", nameof(Role.ADVISOR), ["adv-a-1", "adv-a-2"], "Adam — Acme Wealth Partners, advisor"),
        new("olga", "firm-a", nameof(Role.OPS), [], "Olga — Acme Wealth Partners, billing ops"),
        new("rita", "firm-a", nameof(Role.READ_ONLY), [], "Rita — Acme Wealth Partners, read-only"),
        new("bob", "firm-b", nameof(Role.FIRM_ADMIN), [], "Bob — Northwind Capital, firm admin"),
        new("bianca", "firm-b", nameof(Role.ADVISOR), ["adv-b-1"], "Bianca — Northwind Capital, advisor"),
        new("carol", "firm-c", nameof(Role.FIRM_ADMIN), [], "Carol — Contoso Advisors, firm admin"),
        new("chris", "firm-c", nameof(Role.ADVISOR), ["adv-c-1"], "Chris — Contoso Advisors, advisor"),
    ];

    public static IEndpointRouteBuilder MapDevIssuer(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev").AllowAnonymous();
        dev.MapGet("/users", () => Personas);
        dev.MapPost("/token", (TokenRequest request, IOptions<AuthOptions> auth) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId)
                || !TenantId.TryParse(request.FirmId, out var firm) || firm.IsShared
                || !Enum.TryParse<Role>(request.Role, out var role))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["userId, a firm id (firm-*) and a role (FIRM_ADMIN, ADVISOR, OPS, READ_ONLY) are required."],
                });
            }
            var (token, expires) = DevJwt.Issue(auth.Value, request.UserId, firm, role, request.AdvisorIds ?? []);
            return Results.Ok(new TokenResponse(token, expires));
        });
        return app;
    }
}

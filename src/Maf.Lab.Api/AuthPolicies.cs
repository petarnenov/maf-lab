using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Api;

public static class AuthPolicies
{
    public const string FirmAdmin = "firm-admin";

    public static void Add(Microsoft.AspNetCore.Authorization.AuthorizationOptions options) =>
        options.AddPolicy(FirmAdmin, p => p.RequireAssertion(ctx => PrincipalClaims.TryCreate(ctx.User, out var principal) && principal.IsFirmAdmin));
}

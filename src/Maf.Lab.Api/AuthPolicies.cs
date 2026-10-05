using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Api;

public static class AuthPolicies
{
    public const string TenantAdmin = "tenant-admin";

    public static void Add(Microsoft.AspNetCore.Authorization.AuthorizationOptions options) =>
        options.AddPolicy(TenantAdmin, p => p.RequireAssertion(ctx => PrincipalClaims.TryCreate(ctx.User, out var principal) && principal.IsTenantAdmin));
}

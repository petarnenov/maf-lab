using System.Security.Claims;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.Tests;

public sealed class CompanyIdentityTests
{
    private static ClaimsPrincipal User(string organization, string realm = "{\"roles\":[\"USER\"]}", string? clients = null) => new(new ClaimsIdentity(
        new[] { new Claim("sub", "alice"), new Claim("organization", organization, "JSON"), new Claim("realm_access", realm, "JSON"),
            new Claim("tenant_id", "firm-b"), new Claim("role", "PLATFORM_ADMIN") }
        .Concat(clients is null ? [] : new[] { new Claim("resource_access", clients, "JSON") }), "validated"));

    [Fact]
    public void Organization_and_provisioned_group_ids_override_legacy_claims_without_promoting_group_paths()
    {
        var user = User("{\"firm-a\":{\"groups\":[\"/Finance\",\"/Finance\"]}}");
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("groups", "group-a-id"));
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("domain_roles", "billing:advisor"));
        Assert.True(CompanyIdentityClaims.TryNormalize(user, "api", out var normalized));
        Assert.True(PrincipalClaims.TryCreate(normalized, out var principal));
        Assert.Equal("firm-a", principal.TenantId.Value);
        Assert.Equal(Role.USER, principal.Role);
        Assert.Equal(["group-a-id"], principal.GroupIds);
        Assert.DoesNotContain("/Finance", principal.GroupIds);
        Assert.Equal("billing:advisor", normalized.FindFirst("domain_roles")!.Value);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"firm-a\":{},\"firm-b\":{}}")]
    [InlineData("{\"shared\":{}}")]
    [InlineData("{\"firm-a\":null}")]
    [InlineData("{\"firm-a\":{\"groups\":\"admin\"}}")]
    [InlineData("{\"firm-a\":{\"groups\":[1]}}")]
    [InlineData("malformed")]
    public void Missing_ambiguous_or_malformed_organization_is_refused(string organization) =>
        Assert.False(CompanyIdentityClaims.TryNormalize(User(organization), "api", out _));

    [Fact]
    public void Only_recognized_realm_or_trusted_client_core_roles_are_mapped()
    {
        Assert.True(CompanyIdentityClaims.TryNormalize(User("{\"firm-a\":{}}", "{\"roles\":[\"platform_operator\",\"offline_access\"]}"), "api", out var op));
        Assert.True(PrincipalClaims.TryCreate(op, out var principal));
        Assert.Equal(Role.PLATFORM_ADMIN, principal.Role);
        Assert.True(CompanyIdentityClaims.TryNormalize(User("{\"firm-a\":{}}", "{}", "{\"api\":{\"roles\":[\"TENANT_ADMIN\"]}}"), "api", out var admin));
        Assert.True(PrincipalClaims.TryCreate(admin, out principal));
        Assert.Equal(Role.TENANT_ADMIN, principal.Role);
        Assert.False(CompanyIdentityClaims.TryNormalize(User("{\"firm-a\":{}}", "{}", "{\"other\":{\"roles\":[\"PLATFORM_ADMIN\"]}}"), "api", out _));
        Assert.False(CompanyIdentityClaims.TryNormalize(User("{\"firm-a\":{}}", "{\"roles\":[\"USER\",\"TENANT_ADMIN\"]}"), "api", out _));
        Assert.False(CompanyIdentityClaims.TryNormalize(User("{\"firm-a\":{}}", "{\"roles\":[1]}"), "api", out _));
    }

    [Fact]
    public void Principal_groups_are_immutable_and_equality_compares_membership_instead_of_set_identity()
    {
        var input = new HashSet<string> { "b", "a" };
        var first = new Principal("alice", TenantId.Firm("firm-a"), Role.USER) { GroupIds = input };
        input.Add("mutated");
        var second = first with { GroupIds = new HashSet<string> { "a", "b" } };
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.DoesNotContain("mutated", first.GroupIds);
        Assert.NotEqual(first, first with { GroupIds = new HashSet<string> { "a" } });
    }
}

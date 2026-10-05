using System.Security.Claims;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maf.Lab.Tests;

public class TenancyTests
{
    private static readonly AuthOptions Auth = new();

    [Theory]
    [InlineData("firm-a", true)]
    [InlineData("shared", true)]
    [InlineData("firm-", false)]
    [InlineData("FIRM-A", false)]
    [InlineData("unowned", false)]
    [InlineData(null, false)]
    public void TenantId_parses_only_firms_and_shared(string? value, bool ok) => Assert.Equal(ok, TenantId.TryParse(value, out _));

    [Fact]
    public void Principal_reads_own_tenant_and_shared_only()
    {
        var p = new Principal("u", TenantId.Firm("firm-a"), Role.USER);
        Assert.Equal(["firm-a", "shared"], p.ReadableTenants.Select(t => t.Value));
    }

    [Fact]
    public async Task Valid_token_yields_principal()
    {
        var (token, _) = DevJwt.Issue(Auth, "adam", TenantId.Firm("firm-a"), Role.USER);
        var principal = await ValidateAsync(token);

        Assert.True(PrincipalClaims.TryCreate(principal, out var p));
        Assert.Equal(new Principal("adam", TenantId.Firm("firm-a"), Role.USER), p);
    }

    [Fact]
    public async Task Domain_claims_ride_in_the_token_but_not_in_the_principal()
    {
        var (token, _) = DevJwt.Issue(Auth, "adam", TenantId.Firm("firm-a"), Role.USER,
            domainRoles: ["billing:advisor"], advisorIds: ["adv-a-1", "adv-a-2"]);
        var claims = await ValidateAsync(token);

        Assert.Equal(["billing:advisor"], claims!.FindAll(PrincipalClaims.DomainRoles).Select(c => c.Value));
        Assert.Equal(["adv-a-1", "adv-a-2"], claims.FindAll(PrincipalClaims.AdvisorIds).Select(c => c.Value));
        Assert.True(PrincipalClaims.TryCreate(claims, out var p));
        Assert.Equal(new Principal("adam", TenantId.Firm("firm-a"), Role.USER), p); // the core sees none of them
    }

    [Theory]
    [InlineData("FIRM_ADMIN", Role.TENANT_ADMIN)]
    [InlineData("ADVISOR", Role.USER)]
    [InlineData("OPS", Role.USER)]
    [InlineData("READ_ONLY", Role.READ_ONLY)]
    public void A_token_from_before_the_rename_still_yields_its_principal(string legacyRole, Role expected)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "adam"), new Claim("role", legacyRole), new Claim(PrincipalClaims.LegacyFirmId, "firm-a")], "test"));

        Assert.True(PrincipalClaims.TryCreate(claims, out var p));
        Assert.Equal(new Principal("adam", TenantId.Firm("firm-a"), expected), p);
    }

    [Fact]
    public void Tenant_id_wins_over_the_legacy_firm_id()
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "adam"), new Claim("role", "USER"), new Claim("tenant_id", "firm-b"), new Claim("firm_id", "firm-a")], "test"));

        Assert.True(PrincipalClaims.TryCreate(claims, out var p));
        Assert.Equal("firm-b", p.TenantId.Value);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("tenant_admin")]
    public void Unknown_roles_make_no_principal(string role)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "adam"), new Claim("role", role), new Claim("tenant_id", "firm-a")], "test"));
        Assert.False(PrincipalClaims.TryCreate(claims, out _));
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var (token, _) = DevJwt.Issue(Auth, "adam", TenantId.Firm("firm-a"), Role.USER, DateTimeOffset.UtcNow.AddHours(-10));
        Assert.Null(await ValidateAsync(token));
    }

    [Fact]
    public async Task Token_with_bad_signature_is_rejected()
    {
        var other = new AuthOptions { SigningKey = "another-signing-key-that-is-long-enough-0123456789" };
        var (token, _) = DevJwt.Issue(other, "adam", TenantId.Firm("firm-a"), Role.USER);
        Assert.Null(await ValidateAsync(token));
    }

    [Fact]
    public void Claims_without_a_tenant_or_with_the_shared_tenant_do_not_make_a_principal()
    {
        var noFirm = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x"), new Claim("role", "USER")], "test"));
        var sharedFirm = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x"), new Claim("role", "USER"), new Claim("tenant_id", "shared")], "test"));
        Assert.False(PrincipalClaims.TryCreate(noFirm, out _));
        Assert.False(PrincipalClaims.TryCreate(sharedFirm, out _));
    }

    private static async Task<ClaimsPrincipal?> ValidateAsync(string token)
    {
        var result = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(token, DevJwt.ValidationParameters(Auth));
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }
}

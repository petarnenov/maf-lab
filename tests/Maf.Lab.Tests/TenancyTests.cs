using System.Security.Claims;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
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
    public void Principal_reads_own_firm_and_shared_only()
    {
        var p = new Principal("u", TenantId.Firm("firm-a"), Role.ADVISOR, []);
        Assert.Equal(["firm-a", "shared"], p.ReadableTenants.Select(t => t.Value));
    }

    [Fact]
    public async Task Valid_token_yields_principal()
    {
        var (token, _) = DevJwt.Issue(Auth, "adam", TenantId.Firm("firm-a"), Role.ADVISOR, ["adv-a-1", "adv-a-2"]);
        var principal = await ValidateAsync(token);

        Assert.True(PrincipalClaims.TryCreate(principal, out var p));
        Assert.Equal("adam", p.UserId);
        Assert.Equal("firm-a", p.FirmId.Value);
        Assert.Equal(Role.ADVISOR, p.Role);
        Assert.Equal(["adv-a-1", "adv-a-2"], p.AllowedAdvisorIds);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var (token, _) = DevJwt.Issue(Auth, "adam", TenantId.Firm("firm-a"), Role.ADVISOR, [], DateTimeOffset.UtcNow.AddHours(-10));
        Assert.Null(await ValidateAsync(token));
    }

    [Fact]
    public async Task Token_with_bad_signature_is_rejected()
    {
        var other = new AuthOptions { SigningKey = "another-signing-key-that-is-long-enough-0123456789" };
        var (token, _) = DevJwt.Issue(other, "adam", TenantId.Firm("firm-a"), Role.ADVISOR, []);
        Assert.Null(await ValidateAsync(token));
    }

    [Fact]
    public void Claims_without_firm_or_with_shared_firm_do_not_make_a_principal()
    {
        var noFirm = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x"), new Claim("role", "ADVISOR")], "test"));
        var sharedFirm = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x"), new Claim("role", "ADVISOR"), new Claim("firm_id", "shared")], "test"));
        Assert.False(PrincipalClaims.TryCreate(noFirm, out _));
        Assert.False(PrincipalClaims.TryCreate(sharedFirm, out _));
    }

    private static async Task<ClaimsPrincipal?> ValidateAsync(string token)
    {
        var result = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(token, DevJwt.ValidationParameters(Auth));
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }
}

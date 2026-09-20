using System.Security.Claims;
using Maf.Lab.Api.A2A;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maf.Lab.Tests;

/// <summary>
/// A partner is a system, not a user. The two token kinds are kept apart by their audience — by construction, not
/// by a check someone has to remember — and what a partner may see comes from the server, never from its token.
/// </summary>
public class PartnerIdentityTests
{
    private static readonly AuthOptions Auth = new();

    private static A2AOptions Options(params string[] firms) => new()
    {
        Partners =
        {
            ["acme-portal"] = new PartnerRegistration { Firms = [.. firms], Scopes = [A2AScopes.BillingRead] },
        },
    };

    private static ClaimsPrincipal Read(string token, Microsoft.IdentityModel.Tokens.TokenValidationParameters parameters)
    {
        var result = new JsonWebTokenHandler().ValidateTokenAsync(token, parameters).GetAwaiter().GetResult();
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : throw new InvalidOperationException(result.Exception?.Message);
    }

    private static bool Validates(string token, Microsoft.IdentityModel.Tokens.TokenValidationParameters parameters) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, parameters).GetAwaiter().GetResult().IsValid;

    [Fact]
    public void A_partner_token_validates_for_a2a_and_not_for_chat()
    {
        var a2a = Options("firm-a");
        var (token, expires) = PartnerJwt.Issue(Auth, a2a, "acme-portal", [A2AScopes.BillingRead]);

        Assert.True(Validates(token, PartnerJwt.ValidationParameters(Auth, a2a)));
        // The chat validator rejects it because the audience is different: nothing had to remember to check.
        Assert.False(Validates(token, DevJwt.ValidationParameters(Auth)));
        Assert.True(expires > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void A_chat_token_is_refused_by_the_a2a_validator()
    {
        var (token, _) = DevJwt.Issue(Auth, "adam", TenantId.Firm("firm-a"), Role.ADVISOR, []);

        Assert.False(Validates(token, PartnerJwt.ValidationParameters(Auth, Options("firm-a"))));
    }

    [Fact]
    public void Entitlements_come_from_configuration_not_from_the_token()
    {
        var a2a = Options("firm-a");
        var (token, _) = PartnerJwt.Issue(Auth, a2a, "acme-portal", [A2AScopes.BillingRead]);
        var user = Read(token, PartnerJwt.ValidationParameters(Auth, a2a));

        var partner = PartnerJwt.Resolve(user, a2a)!;

        Assert.Equal("acme-portal", partner.PartnerId);
        Assert.True(partner.MaySee(TenantId.Firm("firm-a")));
        Assert.False(partner.MaySee(TenantId.Firm("firm-b")));
    }

    [Fact]
    public void A_token_claiming_more_than_its_registration_gains_nothing()
    {
        var a2a = Options("firm-a");
        // The token asks for a write scope the registration does not grant, and names a firm nobody asked it about.
        var claims = new ClaimsIdentity(
        [
            new Claim(PartnerClaims.PartnerId, "acme-portal"),
            new Claim(PartnerClaims.Scope, A2AScopes.BillingRead),
            new Claim(PartnerClaims.Scope, A2AScopes.BillingWrite),
            new Claim("firm_id", "firm-b"),
        ]);

        var partner = PartnerJwt.Resolve(new ClaimsPrincipal(claims), a2a)!;

        Assert.True(partner.Has(A2AScopes.BillingRead));
        Assert.False(partner.Has(A2AScopes.BillingWrite));
        Assert.False(partner.MaySee(TenantId.Firm("firm-b")));
        Assert.Equal([TenantId.Firm("firm-a")], partner.AllowedFirms);
    }

    [Fact]
    public void An_unregistered_partner_resolves_to_nothing()
    {
        var claims = new ClaimsIdentity([new Claim(PartnerClaims.PartnerId, "stranger")]);

        Assert.Null(PartnerJwt.Resolve(new ClaimsPrincipal(claims), Options("firm-a")));
        Assert.Null(PartnerJwt.Resolve(null, Options("firm-a")));
    }

    [Fact]
    public void A_partner_is_not_a_principal_and_cannot_stand_in_for_a_user()
    {
        var partner = new PartnerPrincipal("acme-portal", new HashSet<TenantId> { TenantId.Firm("firm-a") },
            new HashSet<string> { A2AScopes.BillingRead });

        // The tenant-scoped query path takes a Principal; a PartnerPrincipal is not one and never converts to one.
        Assert.False(typeof(Principal).IsAssignableFrom(partner.GetType()));
        Assert.DoesNotContain(typeof(PartnerPrincipal).GetMethods(), m => m.Name is "op_Implicit" or "op_Explicit");
        Assert.DoesNotContain(typeof(PartnerPrincipal).GetProperties(), p => p.PropertyType == typeof(Principal));
    }
}

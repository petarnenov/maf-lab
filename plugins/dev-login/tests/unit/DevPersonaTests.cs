using Maf.Lab.Plugins.DevLogin;
using Maf.Lab.Domain.Tenancy;
namespace Maf.Lab.Tests;
public sealed class DevPersonaTests
{
    [Fact]
    public void Dev_personas_keep_their_billing_roles_as_domain_claims()
    {
        var personas = DevIssuerEndpoints.Personas.ToDictionary(p => p.UserId);

        Assert.All(personas.Values, p => Assert.True(PrincipalClaims.TryParseRole(p.Role, out _), p.UserId));
        Assert.Equal(("TENANT_ADMIN", ""), Shape(personas["alice"]));
        Assert.Equal(("USER", "billing:advisor"), Shape(personas["adam"]));
        Assert.Equal(["adv-a-1", "adv-a-2"], personas["adam"].AdvisorIds);
        Assert.Equal(("USER", "billing:ops"), Shape(personas["olga"]));
        Assert.Equal(("READ_ONLY", ""), Shape(personas["rita"]));
        Assert.Equal(("USER", "billing:advisor"), Shape(personas["chris"]));

        static (string, string) Shape(DevIssuerEndpoints.DevUser p) => (p.Role, string.Join(",", p.DomainRoles));
    }

}

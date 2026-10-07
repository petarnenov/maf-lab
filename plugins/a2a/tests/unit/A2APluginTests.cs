using Maf.Lab.A2A;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>What installing the a2a plugin adds to the api, and what it leaves as it was.</summary>
public class A2APluginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_partner_scheme_is_added_and_the_default_stays_the_users()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = A2APluginSupport.Installed };

        var schemes = api.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.NotNull(await schemes.GetSchemeAsync(PartnerJwt.Scheme));
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
    }

    [Fact]
    public async Task The_card_is_served_once_installed()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = A2APluginSupport.Installed };

        var card = await api.CreateClient().GetStringAsync(AgentCardFactory.WellKnownPath, Ct);

        Assert.Contains("\"maf-lab assistant\"", card);
    }
}

using System.Net;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Tests;

/// <summary>The statistics are the plugin's alone: without it installed, neither route is served.</summary>
public class InsightsPluginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/admin/intent-stats")]
    [InlineData("/api/admin/jev-stats")]
    public async Task Without_the_plugin_the_route_is_not_served(string path)
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(path, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/intent-stats")]
    [InlineData("/api/admin/jev-stats")]
    public async Task With_the_plugin_a_firm_admin_reads_it(string path)
    {
        using var api = InsightsPluginSupport.Api(ApiFactory.ProceduralModel());
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path, Ct)).StatusCode);
    }
}

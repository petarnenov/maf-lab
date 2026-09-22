using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

public class InstanceIdentityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Api_responses_carry_the_instance_header_and_health_reports_it()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var me = await client.GetAsync("/api/me", Ct);
        Assert.Equal(Environment.MachineName, me.Headers.GetValues(InstanceIdentity.Header).Single());

        var health = await api.CreateClient().GetFromJsonAsync<JsonElement>("/health", Ct);
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal(Environment.MachineName, health.GetProperty("instance").GetString());

        var unauthorized = await api.CreateClient().GetAsync("/api/me", Ct);
        Assert.True(unauthorized.Headers.Contains(InstanceIdentity.Header));
    }

    [Fact]
    public async Task Mcp_server_responses_carry_the_instance_header()
    {
        await using var server = new WebApplicationFactory<Maf.Lab.Retrieval.Program>()
            .WithWebHostBuilder(b => b.WithFakeSharedState());
        var response = await server.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(Environment.MachineName, response.Headers.GetValues(InstanceIdentity.Header).Single());
        Assert.Equal(Environment.MachineName, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("instance").GetString());
        var mcp = await server.CreateClient().PostAsync("/mcp", new StringContent("{}"), Ct);
        Assert.True(mcp.Headers.Contains(InstanceIdentity.Header));
    }
}

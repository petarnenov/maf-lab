using System.Net;
using System.Net.Http.Json;
using Maf.Lab.A2A;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Tests;

/// <summary>
/// The core without the A2A surface (extract-a2a): no plugin maps the agent card or the protocol, so both are simply not
/// there — the api starts and answers 404, never a 500 from a half-wired server.
/// </summary>
public class ProtocolSurfaceAbsentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_the_surface_the_card_and_the_protocol_are_not_found()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.CreateClient();

        var card = await client.GetAsync(AgentCardFactory.WellKnownPath, Ct);
        var rpc = await client.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method = "message/send" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, card.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rpc.StatusCode);
    }

    [Fact]
    public async Task Without_the_surface_an_admin_cancel_says_there_is_none()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN).PostAsync("/api/admin/a2a/tasks/t-1/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no A2A surface", await response.Content.ReadAsStringAsync(Ct));
    }
}

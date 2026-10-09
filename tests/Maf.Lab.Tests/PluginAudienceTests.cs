using System.Net;
using System.Net.Http.Headers;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public sealed class PluginAudienceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("billing")]
    [InlineData("portfolio")]
    [InlineData("code")]
    public async Task Resource_server_refuses_the_api_token_and_accepts_only_its_bound_audience(string audience)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        // A shared platform setting cannot widen this resource server's required audience.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Audience"] = "api" });
        builder.Services.AddDevJwtAuthentication(builder.Configuration, audience);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/mcp", () => Results.Ok()).RequireAuthorization();
        await app.StartAsync(Ct);
        var client = app.GetTestClient();
        var principal = new Principal("adam", TenantId.Firm("firm-a"), Role.USER);
        string Token(string target) => DevJwt.Issue(new AuthOptions { Audience = target }, principal.UserId, principal.TenantId, principal.Role).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("api"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/mcp", null, Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(audience));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/mcp", null, Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("another-plugin"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/mcp", null, Ct)).StatusCode);
    }
}

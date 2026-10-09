using System.Net;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>The core has no repository test-generation routes or tables without their contributor.</summary>
public sealed class DevelopmentSurfaceAbsentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", "/dev/users")]
    [InlineData("POST", "/dev/token")]
    [InlineData("GET", "/api/topology")]
    [InlineData("GET", "/api/topology/diagram")]
    [InlineData("GET", "/api/admin/feedback/queue")]
    [InlineData("POST", "/api/admin/feedback/a-turn/label")]
    [InlineData("GET", "/api/coverage/tree")]
    [InlineData("GET", "/api/admin/coverage/test-agent")]
    [InlineData("POST", "/api/coverage/refresh")]
    [InlineData("POST", "/api/coverage/runs/agent")]
    public async Task Repository_routes_require_their_contributor(string method, string path)
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN).SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Repository_tables_require_their_contributor()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.GetTableName() is { } table
            && (table.StartsWith("Coverage", StringComparison.Ordinal) || table.StartsWith("TestGen", StringComparison.Ordinal)));
    }
}

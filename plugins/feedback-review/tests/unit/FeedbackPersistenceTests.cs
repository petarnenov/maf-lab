using System.Net;
using System.Net.Http.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public class FeedbackPersistenceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Labels_persist_without_an_exporter_and_cannot_cross_tenants()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = FeedbackReviewPluginSupport.Installed };
        var done = (await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "hello"))[^1].Data;
        var turn = done.GetProperty("runId").GetString()!;
        var request = new LabelRequest(EvalDataset.Selection, [], null, null, null);
        var path = $"/api/admin/feedback/{turn}/label";
        Assert.IsType<NoEvalDataset>(api.Services.GetRequiredService<IAppendEvalDataset>());
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("bob", "firm-b", Role.TENANT_ADMIN).PostAsJsonAsync(path, request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor("adam", "firm-a", Role.USER).PostAsJsonAsync(path, request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN).PostAsJsonAsync(path, request, Ct)).StatusCode);
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var label = Assert.Single(await db.Labels.ToListAsync(Ct));
        Assert.Equal("firm-a", label.TenantId);
        Assert.Equal("alice", label.ReviewerId);
        Assert.True((await db.Turns.SingleAsync(t => t.Id == turn, Ct)).Labeled);
        Assert.False(Directory.Exists(Path.Combine(api.DataDir, "evals")));
    }
}

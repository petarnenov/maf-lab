using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.IndexAdmin;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// The index admin is the plugin's alone, and it indexes only what the installed plugins declare (extract-index-admin-plugin
/// design D4-D6, D9): which corpora are offered, what a request that names none or the wrong one gets, and plugin-off.
/// </summary>
public sealed class IndexAdminPluginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("GET", "/api/platform/index/corpora")]
    [InlineData("GET", "/api/platform/index/status")]
    [InlineData("GET", "/api/platform/index/drift")]
    [InlineData("POST", "/api/platform/index/run")]
    [InlineData("POST", "/api/platform/index/migrate")]
    [InlineData("GET", "/api/platform/jobs/j_any")]
    [InlineData("POST", "/api/platform/jobs/j_any/cancel")]
    public async Task Without_the_plugin_no_route_is_served(string method, string path)
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [IndexAdminPluginSupport.CorpusPlugin("alpha"), .. StandInDomains.Installed] };
        var admin = api.ClientFor("alice", "firm-a", Role.PLATFORM_ADMIN);

        var response = await admin.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_installed_tenant_layout_corpora_are_offered()
    {
        using var api = IndexAdminPluginSupport.Api([
            IndexAdminPluginSupport.CorpusPlugin("beta"),
            IndexAdminPluginSupport.CorpusPlugin("alpha", graph: "alpha"),
            // The repository layout is the installation's (the code corpus): never one tenant's to rebuild.
            IndexAdminPluginSupport.CorpusPlugin("source", CorpusLayoutNames.Repository),
        ]);

        var corpora = await api.ClientFor("alice", "firm-a", Role.PLATFORM_ADMIN)
            .GetFromJsonAsync<List<CorpusView>>("/api/platform/index/corpora", Json, Ct);

        Assert.Equal([new CorpusView("alpha", true), new CorpusView("beta", false)], corpora);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("source")]
    public async Task A_corpus_no_installed_plugin_offers_is_not_found_and_starts_nothing(string corpus)
    {
        using var api = IndexAdminPluginSupport.Api([
            IndexAdminPluginSupport.CorpusPlugin("alpha"),
            IndexAdminPluginSupport.CorpusPlugin("source", CorpusLayoutNames.Repository),
        ]);
        var admin = api.ClientFor("alice", "firm-a", Role.PLATFORM_ADMIN);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/platform/index/run", new { corpus }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/platform/index/migrate", new { corpus }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/platform/index/drift?corpus={corpus}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/platform/index/status?corpus={corpus}", Ct)).StatusCode);
        await using var scope = api.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IAdminJobs>().OpenAsync(IndexAdminPlugin.Kinds, Ct));
    }

    [Fact]
    public async Task With_several_corpora_a_run_that_names_none_is_told_the_choices()
    {
        using var api = IndexAdminPluginSupport.Api([IndexAdminPluginSupport.CorpusPlugin("alpha"), IndexAdminPluginSupport.CorpusPlugin("beta")]);

        var response = await api.ClientFor("alice", "firm-a", Role.PLATFORM_ADMIN).PostAsync("/api/platform/index/run", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        Assert.Equal("Name a corpus: alpha, beta.", problem.GetProperty("errors").GetProperty("corpus")[0].GetString());
    }

    [Fact]
    public async Task With_one_corpus_a_run_that_names_none_runs_it()
    {
        using var api = IndexAdminPluginSupport.Api([IndexAdminPluginSupport.CorpusPlugin("alpha")]);
        var admin = api.ClientFor("alice", "firm-a", Role.PLATFORM_ADMIN);

        var response = await admin.PostAsync("/api/platform/index/run", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        // The corpus folder is absent: the run names it, says it does not exist and changes nothing.
        var finished = await AdminIndexApiTests.WaitAsync(admin, job!.JobId);
        Assert.StartsWith("alpha: indexed 0", finished.Summary);
    }

    [Fact]
    public async Task A_declared_corpus_that_is_absent_removes_nothing()
    {
        // Its folder is not there: the run stops before any store (unreachable here, so touching one would fail the job).
        using var api = IndexAdminPluginSupport.Api([IndexAdminPluginSupport.CorpusPlugin("alpha"), IndexAdminPluginSupport.CorpusPlugin("beta")]);
        IndexAdminPluginSupport.WriteDocument(api, "beta");
        var admin = api.ClientFor("alice", "firm-a", Role.PLATFORM_ADMIN);

        var response = await admin.PostAsJsonAsync("/api/platform/index/run", new { corpus = "alpha" }, Ct);

        var finished = await AdminIndexApiTests.WaitAsync(admin, (await response.Content.ReadFromJsonAsync<AdminJob>(Json, Ct))!.JobId);
        Assert.Equal(AdminJobStates.Succeeded, finished.State);
        Assert.Equal("alpha: indexed 0, unchanged 0, chunks written 0, deleted 0, rejected 1", finished.Summary);
    }

    [Fact]
    public async Task A_named_corpus_is_the_one_run()
    {
        // beta has a document, so its run reaches its store (unreachable here) and fails there; alpha's folder is absent.
        using var api = IndexAdminPluginSupport.Api([IndexAdminPluginSupport.CorpusPlugin("alpha"), IndexAdminPluginSupport.CorpusPlugin("beta")]);
        IndexAdminPluginSupport.WriteDocument(api, "beta");
        var admin = api.ClientFor("alice", "firm-a", Role.PLATFORM_ADMIN);

        var response = await admin.PostAsJsonAsync("/api/platform/index/run", new { corpus = "beta" }, Ct);

        var finished = await AdminIndexApiTests.WaitAsync(admin, (await response.Content.ReadFromJsonAsync<AdminJob>(Json, Ct))!.JobId);
        Assert.Equal(AdminJobStates.Failed, finished.State);
    }

    [Fact]
    public async Task Plugin_off_sees_an_open_job_and_cancels_it_through_the_store()
    {
        using var api = IndexAdminPluginSupport.Api([IndexAdminPluginSupport.CorpusPlugin("alpha")]);
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var runner = api.Services.GetRequiredService<Maf.Lab.Api.Admin.AdminJobRunner>();
        var job = await runner.StartAsync("firm-a", IndexAdminPlugin.IndexKind, async ct => { await Task.Delay(Timeout.Infinite, ct); return "never"; }, Ct);
        // Another plugin's job kind is not this plugin's open work.
        var other = await runner.StartAsync("firm-a", "coverage.refresh", async ct => { await Task.Delay(Timeout.Infinite, ct); return "never"; }, Ct);

        var open = await admin.GetFromJsonAsync<List<OpenWorkItem>>($"/api/plugins/{IndexAdminPlugin.PluginName}/open-work", Json, Ct);
        Assert.Equal([new OpenWorkItem(IndexAdminPlugin.IndexKind, job.JobId, AdminJobStates.Running)], open);

        Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsync($"/api/plugins/{IndexAdminPlugin.PluginName}/open-work/cancel", null, Ct)).StatusCode);

        Assert.Equal(AdminJobStates.Canceled, (await runner.GetAsync("firm-a", job.JobId, Ct))!.State);
        Assert.Equal(AdminJobStates.Running, (await runner.GetAsync("firm-a", other.JobId, Ct))!.State);
        Assert.Empty((await admin.GetFromJsonAsync<List<OpenWorkItem>>($"/api/plugins/{IndexAdminPlugin.PluginName}/open-work", Json, Ct))!);
        await runner.CancelAsync("firm-a", other.JobId, Ct);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// The admin index API over a real host and job runner (SQLite in a temp folder): what a FIRM_ADMIN's screen
/// gets from /api/admin/index/* and /api/admin/jobs/{id}. The vector store is unreachable in these tests, so a
/// started job ends failed; what each test asserts is the endpoint's own contract.
/// </summary>
public sealed class AdminIndexApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A second dense vector, as a deployment preparing a model migration would configure.</summary>
    private static Dictionary<string, string?> TwoVectors() => new()
    {
        ["Models:Embeddings:dense_alt:Model"] = "alt-model",
        ["Models:Embeddings:dense_alt:Dimensions"] = "384",
    };

    private static ApiFactory AdminApi(Dictionary<string, string?>? extra = null) =>
        new(ApiFactory.ProceduralModel()) { ExtraSettings = extra ?? new Dictionary<string, string?>() };

    [Fact]
    public async Task The_admin_index_surface_is_for_firm_admins()
    {
        using var api = AdminApi();

        var response = await api.ClientFor("bob", "firm-a", Role.USER).GetAsync("/api/admin/index/status", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_running_job_is_stopped_through_its_cancel_route()
    {
        using var api = AdminApi();
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var runner = api.Services.GetRequiredService<Maf.Lab.Api.Admin.AdminJobRunner>();
        var job = await runner.StartAsync("firm-a", "index", async ct => { await Task.Delay(Timeout.Infinite, ct); return "never"; }, Ct);

        var response = await admin.PostAsync($"/api/admin/jobs/{job.JobId}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(AdminJobStates.Canceled, (await response.Content.ReadFromJsonAsync<AdminJob>(Json, Ct))!.State);
        // Once ended, a second cancel is a conflict; an unknown job is not found; a non-admin may not.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/jobs/{job.JobId}/cancel", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/admin/jobs/j_nope/cancel", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor("bob", "firm-a", Role.USER)
            .PostAsync($"/api/admin/jobs/{job.JobId}/cancel", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_migrate_without_a_target_is_answered_with_a_validation_problem()
    {
        // One profile, and it is the active vector: there is no other target to fall back to.
        using var api = AdminApi();

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN)
            .PostAsync("/api/admin/index/migrate", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        Assert.Equal("Unknown target; use a configured dense vector name or model.",
            problem.GetProperty("errors").GetProperty("targetModel")[0].GetString());
    }

    [Fact]
    public async Task A_migrate_to_an_unknown_name_is_answered_with_a_validation_problem()
    {
        using var api = AdminApi(TwoVectors());

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN)
            .PostAsJsonAsync("/api/admin/index/migrate", new { targetModel = "not-a-vector" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        Assert.Equal("Unknown target; use a configured dense vector name or model.",
            problem.GetProperty("errors").GetProperty("targetModel")[0].GetString());
    }

    [Fact]
    public async Task A_migrate_with_a_malformed_body_counts_as_no_target()
    {
        using var api = AdminApi();

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN)
            .PostAsync("/api/admin/index/migrate", new StringContent("{not json", Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task A_migrate_to_a_configured_vector_name_starts_a_job_that_can_be_polled()
    {
        using var api = AdminApi(TwoVectors());
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);

        var response = await admin.PostAsJsonAsync("/api/admin/index/migrate", new { targetModel = "dense_alt" }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        Assert.NotNull(job);
        Assert.Equal("migrate", job.Kind);
        Assert.Equal($"/api/admin/jobs/{job.JobId}", response.Headers.Location?.ToString());
        var finished = await WaitAsync(admin, job.JobId);
        // The vector store is unreachable in these tests: the job is reported failed, not running forever.
        Assert.Equal(AdminJobStates.Failed, finished.State);
    }

    [Fact]
    public async Task A_migrate_may_name_the_model_instead_of_the_vector()
    {
        using var api = AdminApi(TwoVectors());
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);

        var response = await admin.PostAsJsonAsync("/api/admin/index/migrate", new { targetModel = "alt-model" }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        Assert.NotNull(job);
        Assert.Equal("migrate", job.Kind);
        await WaitAsync(admin, job.JobId);
    }

    [Fact]
    public async Task A_migrate_with_no_body_targets_the_configured_vector_that_is_not_active()
    {
        using var api = AdminApi(TwoVectors());
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);

        var response = await admin.PostAsync("/api/admin/index/migrate", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        Assert.NotNull(job);
        Assert.Equal("migrate", job.Kind);
        await WaitAsync(admin, job.JobId);
    }

    [Fact]
    public async Task An_index_run_is_started_as_a_job_that_can_be_polled()
    {
        // A corpus of one document, so the run reaches the store (unreachable here) and fails there: a run over a missing
        // corpus stops before it, and succeeds with nothing to do.
        var corpus = Directory.CreateTempSubdirectory("maf-lab-admin-corpus-");
        Directory.CreateDirectory(Path.Combine(corpus.FullName, "firm-a", "docs"));
        File.WriteAllText(Path.Combine(corpus.FullName, "firm-a", "docs", "fees.md"), "# Fees\n\nA fee schedule is assigned per account.\n");
        using var api = AdminApi(new Dictionary<string, string?> { ["Indexing:CorpusRoot"] = corpus.FullName });
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);

        var response = await admin.PostAsync("/api/admin/index/run", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        Assert.NotNull(job);
        Assert.Equal("index", job.Kind);
        Assert.Equal($"/api/admin/jobs/{job.JobId}", response.Headers.Location?.ToString());
        var finished = await WaitAsync(admin, job.JobId);
        Assert.Equal(AdminJobStates.Failed, finished.State);
        corpus.Delete(recursive: true);
    }

    [Fact]
    public async Task An_unknown_job_is_not_found()
    {
        using var api = AdminApi();

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN).GetAsync("/api/admin/jobs/j_missing", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<AdminJob> WaitAsync(HttpClient client, string jobId)
    {
        for (var i = 0; i < 400; i++)
        {
            var job = await client.GetFromJsonAsync<AdminJob>($"/api/admin/jobs/{jobId}", Json, Ct);
            if (job is { } done && done.State is AdminJobStates.Succeeded or AdminJobStates.Failed)
            {
                return done;
            }
            await Task.Delay(25, Ct);
        }
        throw new TimeoutException("admin job did not finish");
    }
}
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>The Coverage API: tree, file detail, history, upload and refresh (coverage-ingestion, coverage-dashboard).</summary>
// Heavy (git, several hosts): run one after another rather than beside the timing-sensitive tests.
[Collection("TestGeneration")]
public sealed class CoverageApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static Task<TempGitRepo> RepoAsync() => TempGitRepo.CreateAsync(new Dictionary<string, string>
    {
        ["src/Lab/Small.cs"] = CoverageApi.Lines(10),
        ["src/Lab/Large.cs"] = CoverageApi.Lines(90),
        ["web/src/App.tsx"] = "export const App = () => null;\n",
    }, Ct);

    [Fact]
    public async Task The_tree_aggregates_folders_by_lines_not_by_files()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);
        await CoverageApi.IngestAsync(api, await repo.HeadAsync(Ct), Toolchains.Dotnet, SnapshotKind.Official, null,
            ("src/Lab/Small.cs", 10, 10), ("src/Lab/Large.cs", 0, 90));

        var tree = await api.ClientFor("bob", "firm-a", Role.ADVISOR).GetFromJsonAsync<CoverageTreeDto>("/api/coverage/tree", Json, Ct);

        Assert.True(tree!.HasSnapshot);
        Assert.Equal(10.0, tree.Folders.Single(f => f.Path == "src/Lab").Pct);
        var small = tree.Files.Single(f => f.Path == "src/Lab/Small.cs");
        Assert.Equal((100.0, 80, false, false), (small.Pct, small.Threshold, small.ThresholdIsOverride, small.BelowThreshold));
        Assert.True(tree.Files.Single(f => f.Path == "src/Lab/Large.cs").BelowThreshold);
    }

    [Fact]
    public async Task Two_candidates_at_once_each_show_their_own_files_coverage()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);
        var commit = await repo.HeadAsync(Ct);
        await CoverageApi.IngestAsync(api, commit, Toolchains.Dotnet, SnapshotKind.Official, null,
            ("src/Lab/Small.cs", 5, 10), ("src/Lab/Large.cs", 0, 90));
        // A candidate's measurement covers the whole project, not just the file its run is for.
        await CoverageApi.IngestAsync(api, commit, Toolchains.Dotnet, SnapshotKind.Candidate, "r_small",
            ("src/Lab/Small.cs", 9, 10), ("src/Lab/Large.cs", 0, 90));
        await CoverageApi.IngestAsync(api, commit, Toolchains.Dotnet, SnapshotKind.Candidate, "r_large",
            ("src/Lab/Small.cs", 5, 10), ("src/Lab/Large.cs", 81, 90));
        await using (var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct))
        {
            db.TestGenRuns.Add(CoverageStorageTests.Run("r_small", "src/Lab/Small.cs", TestGenRunState.Candidate));
            db.TestGenRuns.Add(CoverageStorageTests.Run("r_large", "src/Lab/Large.cs", TestGenRunState.Candidate));
            await db.SaveChangesAsync(Ct);
        }

        var tree = await api.ClientFor("bob", "firm-a", Role.ADVISOR).GetFromJsonAsync<CoverageTreeDto>("/api/coverage/tree", Json, Ct);

        var small = tree!.Files.Single(f => f.Path == "src/Lab/Small.cs").Candidate!;
        var large = tree.Files.Single(f => f.Path == "src/Lab/Large.cs").Candidate!;
        Assert.Equal(("r_small", 90.0), (small.RunId, small.Pct));
        Assert.Equal(("r_large", 90.0), (large.RunId, large.Pct));
    }

    [Fact]
    public async Task An_empty_store_says_there_is_no_snapshot()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);

        var tree = await api.ClientFor("bob", "firm-a", Role.ADVISOR).GetFromJsonAsync<CoverageTreeDto>("/api/coverage/tree", Json, Ct);

        Assert.False(tree!.HasSnapshot);
        Assert.Empty(tree.Files);
    }

    [Fact]
    public async Task Coverage_needs_a_signed_in_user()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);

        var response = await api.CreateClient().GetAsync("/api/coverage/tree", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task File_detail_has_the_source_at_the_snapshot_commit_and_line_statuses()
    {
        var repo = await RepoAsync();
        var first = await repo.HeadAsync(Ct);
        using var api = CoverageApi.Create(repo);
        await CoverageApi.IngestAsync(api, first, Toolchains.Dotnet, SnapshotKind.Official, null, ("src/Lab/Small.cs", 4, 10));
        // The working tree moves on; the detail still shows the file as it was measured.
        await repo.CommitAsync(new Dictionary<string, string> { ["src/Lab/Small.cs"] = "changed\n" }, "change", Ct);

        var detail = await api.ClientFor("bob", "firm-a", Role.ADVISOR)
            .GetFromJsonAsync<CoverageEndpoints.FileDetailDto>("/api/coverage/files?path=src/Lab/Small.cs", Json, Ct);

        Assert.Equal(first, detail!.Commit);
        Assert.StartsWith("// line 1\n", detail.Source);
        Assert.Equal((4, 10, 40.0, 80), (detail.Summary.LinesCovered, detail.Summary.LinesTotal, detail.Summary.Pct, detail.Summary.Threshold));
        Assert.Equal([LineStatus.Covered, LineStatus.Uncovered], [detail.Lines[3].Status, detail.Lines[4].Status]);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("src/Lab/../../../etc/passwd")]
    [InlineData("src/Lab/Unknown.cs")]
    public async Task Only_files_a_snapshot_has_are_served(string path)
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);
        await CoverageApi.IngestAsync(api, await repo.HeadAsync(Ct), Toolchains.Dotnet, SnapshotKind.Official, null, ("src/Lab/Small.cs", 4, 10));

        var response = await api.ClientFor("bob", "firm-a", Role.ADVISOR).GetAsync($"/api/coverage/files?path={Uri.EscapeDataString(path)}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("root:", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_file_measured_at_a_commit_the_repository_lacks_is_a_conflict_that_names_the_commit()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);
        // What a ci-e2e clone's merge left behind: measured at a commit the repository then no longer has.
        var missing = await repo.CommitAsync(new Dictionary<string, string> { ["src/Lab/Small.cs"] = "merged\n" }, "merge", Ct);
        await CoverageApi.IngestAsync(api, missing, Toolchains.Dotnet, SnapshotKind.Official, null, ("src/Lab/Small.cs", 4, 10));
        await repo.GitAsync(Ct, "reset", "-q", "--hard", "HEAD~1");
        await repo.GitAsync(Ct, "reflog", "expire", "--expire=now", "--all");
        await repo.GitAsync(Ct, "gc", "-q", "--prune=now");

        var response = await api.ClientFor("bob", "firm-a", Role.ADVISOR).GetAsync("/api/coverage/files?path=src/Lab/Small.cs", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        Assert.Equal("source_unavailable", problem.GetProperty("type").GetString());
        Assert.Equal(missing, problem.GetProperty("commit").GetString());
        Assert.Contains(missing[..12], problem.GetProperty("detail").GetString());
        Assert.DoesNotContain(repo.Root, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task History_is_newest_first()
    {
        var repo = await RepoAsync();
        var commit = await repo.HeadAsync(Ct);
        using var api = CoverageApi.Create(repo);
        await CoverageApi.IngestAsync(api, commit, Toolchains.Dotnet, SnapshotKind.Official, null, ("src/Lab/Small.cs", 2, 10));
        await Task.Delay(20, Ct);
        await CoverageApi.IngestAsync(api, commit, Toolchains.Dotnet, SnapshotKind.Official, null, ("src/Lab/Small.cs", 7, 10));

        var history = await api.ClientFor("bob", "firm-a", Role.ADVISOR)
            .GetFromJsonAsync<List<CoverageEndpoints.HistoryEntryDto>>("/api/coverage/files/history?path=src/Lab/Small.cs", Json, Ct);

        Assert.Equal([70.0, 20.0], history!.Select(h => h.Pct));
    }

    private static MultipartFormDataContent Upload(string commit, string toolchain, string xml)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(commit), "commit" },
            { new StringContent(toolchain), "toolchain" },
            { new StringContent("/work/job"), "root" },
        };
        form.Add(new StringContent(xml), "report", "cobertura.xml");
        return form;
    }

    [Fact]
    public async Task An_admin_can_upload_a_report()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);
        var xml = FakeCoverageRunner.Report("/work/job", ("src/Lab/Small.cs", 5, 10), ("web/src/App.tsx", 1, 1), ("/etc/x.cs", 0, 1));

        var response = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN)
            .PostAsync("/api/coverage/reports", Upload(await repo.HeadAsync(Ct), Toolchains.Dotnet, xml), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CoverageEndpoints.IngestResponse>(Json, Ct);
        // The .tsx belongs to the other toolchain; /work/job/etc/x.cs names nothing in the repository.
        Assert.Equal((1, 1), (result!.Files, result.Dropped));
    }

    [Fact]
    public async Task A_non_admin_cannot_upload()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);

        var response = await api.ClientFor("bob", "firm-a", Role.ADVISOR)
            .PostAsync("/api/coverage/reports", Upload(await repo.HeadAsync(Ct), Toolchains.Dotnet, FakeCoverageRunner.EmptyReport), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await api.Services.GetRequiredService<CoverageStore>().AnyAsync(Ct));
    }

    [Fact]
    public async Task A_malformed_upload_is_rejected_and_nothing_is_stored()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);

        var response = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN)
            .PostAsync("/api/coverage/reports", Upload(await repo.HeadAsync(Ct), Toolchains.Dotnet, "<not-cobertura/>"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await api.Services.GetRequiredService<CoverageStore>().AnyAsync(Ct));
    }

    [Fact]
    public async Task Refresh_measures_both_toolchains_at_main()
    {
        var repo = await RepoAsync();
        var runner = new FakeCoverageRunner
        {
            Answer = r => FakeCoverageRunner.Result(r.Toolchain == Toolchains.Dotnet
                ? FakeCoverageRunner.Report("/work/job", ("src/Lab/Small.cs", 9, 10))
                : FakeCoverageRunner.Report("/work/job/web", ("web/src/App.tsx", 1, 1))),
        };
        using var api = CoverageApi.Create(repo, runner);
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

        var started = await (await admin.PostAsync("/api/coverage/refresh", null, Ct)).Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        var job = await WaitAsync(admin, started!.JobId);

        Assert.Equal(AdminJobStates.Succeeded, job.State);
        var main = await repo.HeadAsync(Ct, "main");
        Assert.All(runner.Requests, r => Assert.Equal(main, r.Commit));
        Assert.Equal([Toolchains.Dotnet, Toolchains.Vitest], runner.Requests.Select(r => r.Toolchain));
        var tree = await admin.GetFromJsonAsync<CoverageTreeDto>("/api/coverage/tree", Json, Ct);
        Assert.Equal(["src/Lab/Small.cs", "web/src/App.tsx"], tree!.Files.Select(f => f.Path));
    }

    [Theory]
    [InlineData(true, "The coverage runner could not be reached.")]
    [InlineData(false, "Neither toolchain produced a coverage report.")]
    public async Task A_refresh_that_fails_names_its_reason(bool runnerDown, string reason)
    {
        var repo = await RepoAsync();
        var runner = new FakeCoverageRunner { Down = runnerDown, Answer = _ => FakeCoverageRunner.Result("", status: "error") };
        using var api = CoverageApi.Create(repo, runner);
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

        var started = await (await admin.PostAsync("/api/coverage/refresh", null, Ct)).Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        var job = await WaitAsync(admin, started!.JobId);

        Assert.Equal((AdminJobStates.Failed, reason), (job.State, job.Summary));
    }

    [Fact]
    public async Task A_second_refresh_while_one_runs_is_answered_with_the_first()
    {
        var repo = await RepoAsync();
        var runner = new FakeCoverageRunner { Gate = new TaskCompletionSource() };
        using var api = CoverageApi.Create(repo, runner);
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

        var first = await (await admin.PostAsync("/api/coverage/refresh", null, Ct)).Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        var second = await (await admin.PostAsync("/api/coverage/refresh", null, Ct)).Content.ReadFromJsonAsync<AdminJob>(Json, Ct);
        runner.Gate.SetResult();
        await WaitAsync(admin, first!.JobId);

        Assert.Equal(first.JobId, second!.JobId);
        // One refresh: one request per toolchain, not two.
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task A_refresh_is_for_admins()
    {
        var repo = await RepoAsync();
        using var api = CoverageApi.Create(repo);

        var response = await api.ClientFor("bob", "firm-a", Role.ADVISOR).PostAsync("/api/coverage/refresh", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<AdminJob> WaitAsync(HttpClient client, string jobId)
    {
        for (var i = 0; i < 200; i++)
        {
            var job = await client.GetFromJsonAsync<AdminJob>($"/api/coverage/refresh/{jobId}", Json, Ct);
            if (job!.State != AdminJobStates.Running)
            {
                return job;
            }
            await Task.Delay(25, Ct);
        }
        throw new TimeoutException("refresh did not finish");
    }
}

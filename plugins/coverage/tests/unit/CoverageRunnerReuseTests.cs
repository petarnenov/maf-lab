extern alias service;
using Maf.Lab.Plugins.Coverage;
using System.Net.Http.Json;
using service::Maf.Lab.CoverageRunner;
using Maf.Lab.TestGen;

namespace Maf.Lab.Tests;

/// <summary>A clock the test moves.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>An identical request reuses a complete result (coverage-runner).</summary>
public sealed partial class CoverageRunnerTests
{
    private const string Target = "src/Lab/Calc.cs";

    [Fact]
    public async Task An_identical_request_is_answered_from_the_kept_result()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);
        var request = new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), Target, TestScope.All);

        var first = await runner.RunAsync(request);
        var second = await runner.RunAsync(request);

        Assert.Single(toolchain.Plans);
        Assert.Null(first.ReusedFrom);
        Assert.NotNull(second.ReusedFrom);
        Assert.StartsWith("job_", second.ReusedFrom.JobId, StringComparison.Ordinal);
        Assert.Equal((first.Tests, first.TargetPct, first.DurationMs, first.CoberturaXml), (second.Tests, second.TargetPct, second.DurationMs, second.CoberturaXml));
    }

    [Fact]
    public async Task A_request_that_differs_in_diff_target_or_scope_runs_again()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);
        var commit = await repo.HeadAsync(Ct);
        var request = new RunnerRequest(commit, "dotnet", AddTest("CalcTests"), Target, TestScope.All);

        await runner.RunAsync(request);
        var results = new[]
        {
            await runner.RunAsync(request with { Diff = AddTest("OtherTests") }),
            await runner.RunAsync(request with { TargetFile = "tests/Lab.Tests/Existing.cs" }),
            await runner.RunAsync(request with { Tests = TestScope.Related }),
        };

        Assert.Equal(4, toolchain.Plans.Count);
        Assert.All(results, r => Assert.Null(r.ReusedFrom));
    }

    [Fact]
    public void The_key_changes_with_every_part_of_the_request_and_only_those()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var request = new RunnerRequest(commit, "dotnet", "diff --git a/x b/x\n", Target, TestScope.Related);
        var key = JobQueue.KeyOf(request);

        Assert.NotNull(key);
        Assert.Equal(key, JobQueue.KeyOf(request with { Fresh = true }));
        Assert.Equal(JobQueue.KeyOf(request with { Tests = null }), JobQueue.KeyOf(request with { Tests = TestScope.All }));
        Assert.Equal(JobQueue.KeyOf(request with { Diff = null }), JobQueue.KeyOf(request with { Diff = "" }));
        var others = new[]
        {
            request with { Commit = "1123456789abcdef0123456789abcdef01234567" },
            request with { Toolchain = "vitest" },
            request with { Diff = "diff --git a/y b/y\n" },
            request with { TargetFile = "src/Lab/Other.cs" },
            request with { TargetFile = null },
            request with { Tests = TestScope.All },
        };
        Assert.All(others, other => Assert.NotEqual(key, JobQueue.KeyOf(other)));
        Assert.Equal(others.Length, others.Select(JobQueue.KeyOf).Distinct().Count());
    }

    [Fact]
    public async Task An_abbreviated_commit_is_never_reused()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);
        var request = new RunnerRequest((await repo.HeadAsync(Ct))[..12], "dotnet", AddTest("CalcTests"), Target);

        await runner.RunAsync(request);
        var second = await runner.RunAsync(request);

        Assert.Equal(2, toolchain.Plans.Count);
        Assert.Null(second.ReusedFrom);
    }

    [Fact]
    public async Task A_timed_out_result_is_not_reused()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { OverrunFirst = 1 };
        await using var runner = new Runner(repo, toolchain);
        var request = new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), Target);

        var first = await runner.RunAsync(request);
        var second = await runner.RunAsync(request);

        Assert.Equal(RunnerStatus.TimedOut, first.Status);
        Assert.True(second.Green);
        Assert.Null(second.ReusedFrom);
        Assert.Equal(2, toolchain.Plans.Count);
    }

    [Fact]
    public async Task A_result_with_a_failing_test_is_not_reused()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { FailingTests = 1 };
        await using var runner = new Runner(repo, toolchain);
        var request = new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), Target);

        await runner.RunAsync(request);
        var second = await runner.RunAsync(request);

        Assert.Equal(1, second.Tests.Failed);
        Assert.Null(second.ReusedFrom);
        Assert.Equal(2, toolchain.Plans.Count);
    }

    [Fact]
    public async Task Two_identical_requests_at_once_run_the_tests_once()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { Gate = new TaskCompletionSource() };
        await using var runner = new Runner(repo, toolchain);
        var client = await runner.ClientAsync();
        var request = new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), Target, TestScope.All);

        var first = await (await client.PostAsJsonAsync("/runs", request, Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
        while (toolchain.Plans.IsEmpty)
        {
            await Task.Delay(10, Ct);
        }
        var second = await (await client.PostAsJsonAsync("/runs", request, Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
        toolchain.Gate.SetResult();
        var firstResult = await PollAsync(client, first!.Id);
        var secondResult = await PollAsync(client, second!.Id);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal((RunnerJobState.Running, 0), (second.State, second.QueuePosition));
        Assert.Single(toolchain.Plans);
        Assert.Null(firstResult.ReusedFrom);
        Assert.Equal(first.Id, secondResult.ReusedFrom?.JobId);
        Assert.Equal(firstResult.Tests, secondResult.Tests);
    }

    [Fact]
    public async Task A_request_waiting_for_one_that_times_out_runs_on_its_own()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { Gate = new TaskCompletionSource(), OverrunFirst = 1 };
        await using var runner = new Runner(repo, toolchain);
        var client = await runner.ClientAsync();
        var request = new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), Target);

        var first = await (await client.PostAsJsonAsync("/runs", request, Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
        while (toolchain.Plans.IsEmpty)
        {
            await Task.Delay(10, Ct);
        }
        var second = await (await client.PostAsJsonAsync("/runs", request, Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
        toolchain.Gate.SetResult();
        var firstResult = await PollAsync(client, first!.Id);
        var secondResult = await PollAsync(client, second!.Id);

        Assert.Equal(RunnerStatus.TimedOut, firstResult.Status);
        Assert.True(secondResult.Green);
        Assert.Null(secondResult.ReusedFrom);
        Assert.Equal(2, toolchain.Plans.Count);
    }

    [Fact]
    public async Task A_kept_result_expires_with_the_window()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await using var runner = new Runner(repo, toolchain, clock);
        var request = new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), Target);

        await runner.RunAsync(request);
        clock.Advance(TimeSpan.FromMinutes(14));
        var within = await runner.RunAsync(request);
        clock.Advance(TimeSpan.FromMinutes(2));
        var after = await runner.RunAsync(request);

        Assert.NotNull(within.ReusedFrom);
        Assert.Null(after.ReusedFrom);
        Assert.Equal(2, toolchain.Plans.Count);
    }

    [Fact]
    public async Task Only_so_many_results_are_kept_and_the_oldest_goes_first()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await using var runner = new Runner(repo, toolchain, clock, new Dictionary<string, string?> { ["Runner:ReuseMaxResults"] = "1" });
        var commit = await repo.HeadAsync(Ct);
        var a = new RunnerRequest(commit, "dotnet", AddTest("ATests"), Target);
        var b = new RunnerRequest(commit, "dotnet", AddTest("BTests"), Target);

        await runner.RunAsync(a);
        clock.Advance(TimeSpan.FromSeconds(1));
        await runner.RunAsync(b);
        var again = await runner.RunAsync(a);

        Assert.Null(again.ReusedFrom);
        Assert.Equal(3, toolchain.Plans.Count);
    }

    [Fact]
    public async Task A_fresh_request_runs_and_its_result_replaces_the_kept_one()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);
        var request = new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), Target, TestScope.All);

        await runner.RunAsync(request);
        var fresh = await runner.RunAsync(request with { Fresh = true });
        var after = await runner.RunAsync(request);

        Assert.Null(fresh.ReusedFrom);
        Assert.Equal(2, toolchain.Plans.Count);
        Assert.NotNull(after.ReusedFrom);
    }

    [Fact]
    public async Task Reuse_set_to_zero_runs_every_request()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain, settings: new Dictionary<string, string?> { ["Runner:ReuseResultsFor"] = "00:00:00" });
        var request = new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), Target);

        await runner.RunAsync(request);
        var second = await runner.RunAsync(request);

        Assert.Null(second.ReusedFrom);
        Assert.Equal(2, toolchain.Plans.Count);
    }

    private static async Task<RunnerResult> PollAsync(HttpClient client, string id)
    {
        while (true)
        {
            var job = await client.GetFromJsonAsync<RunnerJob>($"/runs/{id}", Json, Ct);
            if (job is { State: RunnerJobState.Done, Result: { } result })
            {
                return result;
            }
            await Task.Delay(10, Ct);
        }
    }
}

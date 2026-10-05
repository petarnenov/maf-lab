using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.A2A;
using Maf.Lab.CoverageRunner;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.TestGen;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>
/// A toolchain that looks at the workspace instead of building it: it records which test files it found, writes a
/// Cobertura report crediting the target with one line per test file, and can be held or made to overrun.
/// </summary>
internal sealed class FakeToolchain : IToolchainRunner
{
    public string Toolchain => "dotnet";
    public ConcurrentQueue<string[]> SeenTestFiles { get; } = new();
    public ConcurrentQueue<TestPlan> Plans { get; } = new();
    public TaskCompletionSource? Gate { get; set; }
    public bool Overrun { get; set; }
    /// <summary>The build warnings it reports, as a real build prints them.</summary>
    public IReadOnlyList<string>? Warnings { get; set; }

    /// <summary>A related run reports no test at all, as a filter that matched nothing would.</summary>
    public bool RelatedRunsNothing { get; set; }

    /// <summary>How many of the first runs overrun the time limit; later runs do not.</summary>
    public int OverrunFirst { get; set; }

    /// <summary>How many of its tests fail.</summary>
    public int FailingTests { get; set; }

    public async Task<ToolchainOutcome> RunAsync(string workspace, string outputDir, TestPlan plan, TimeSpan timeLimit, CancellationToken ct)
    {
        var tests = Directory.Exists(Path.Combine(workspace, "tests"))
            ? Directory.GetFiles(Path.Combine(workspace, "tests"), "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(workspace, f)).Order().ToArray()
            : [];
        SeenTestFiles.Enqueue(tests);
        Plans.Enqueue(plan);
        var index = Plans.Count - 1;
        if (plan.Related && RelatedRunsNothing)
        {
            return new ToolchainOutcome(BuildOutcome.Ok, [], new TestCounts(0, 0, 0), [], null, false);
        }
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }
        if (Overrun || index < OverrunFirst)
        {
            return new ToolchainOutcome(BuildOutcome.Ok, [], new TestCounts(0, 0, 0), [], null, TimedOut: true);
        }
        var report = Path.Combine(outputDir, "dotnet.cobertura.xml");
        await File.WriteAllTextAsync(report,
            FakeCoverageRunner.Report(workspace, ("src/Lab/Calc.cs", Math.Min(4, tests.Length), 4)), ct);
        var failures = Enumerable.Range(1, FailingTests).Select(i => new TestFailure($"Lab.Tests.Flaky.Fails{i}", "Assert.True() Failure")).ToList();
        return new ToolchainOutcome(BuildOutcome.Ok, [], new TestCounts(tests.Length, FailingTests, 0), failures, report, false, Warnings: Warnings);
    }
}

/// <summary>The coverage runner as its callers meet it (coverage-runner).</summary>
// Heavy (git, several hosts): run one after another rather than beside the timing-sensitive tests.
[Collection("TestGeneration")]
public sealed partial class CoverageRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Runner(TempGitRepo repo, FakeToolchain toolchain, TimeProvider? clock = null,
        IReadOnlyDictionary<string, string?>? settings = null) : IAsyncDisposable
    {
        private WebApplication? _app;

        /// <summary>Where the runner checks out its workspaces: empty once no job is running.</summary>
        public string WorkRoot { get; } = Directory.CreateTempSubdirectory("maf-runner-").FullName;

        public async Task<HttpClient> ClientAsync(string? partner = "maf-lab-test-agent")
        {
            if (_app is null)
            {
                _app = Maf.Lab.CoverageRunner.Program.BuildApp(ProjectDir.ContentRootArgs("Maf.Lab.CoverageRunner"), builder =>
                {
                    builder.WebHost.UseUrls("http://127.0.0.1:0");
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Runner:RepoRoot"] = repo.Root,
                        ["Runner:WorkRoot"] = WorkRoot,
                        ["Runner:MaxConcurrent"] = "1",
                        ["Runner:DotnetTestProject"] = "tests/Lab.Tests",
                    });
                    if (settings is not null)
                    {
                        builder.Configuration.AddInMemoryCollection(settings);
                    }
                    if (clock is not null)
                    {
                        builder.Services.AddSingleton(clock);
                    }
                    builder.Logging.ClearProviders();
                    builder.Services.AddSingleton<IToolchainRunner>(toolchain);
                });
                await _app.StartAsync();
            }
            var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            var client = new HttpClient { BaseAddress = new Uri(address) };
            if (partner is not null)
            {
                var audience = new A2AOptions { Audience = CoverageRunnerClient.Audience };
                var (token, _) = PartnerJwt.Issue(new AuthOptions(), audience, partner, [CoverageRunnerClient.ScopeRun]);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            return client;
        }

        public async Task<RunnerResult> RunAsync(RunnerRequest request)
        {
            var http = await ClientAsync();
            var token = http.DefaultRequestHeaders.Authorization!.Parameter!;
            return await new CoverageRunnerClient(http, _ => Task.FromResult(token), TimeSpan.FromMilliseconds(20)).RunAsync(request, Ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }

    private static Task<TempGitRepo> RepoAsync() => TempGitRepo.CreateAsync(new Dictionary<string, string>
    {
        ["src/Lab/Calc.cs"] = "a\nb\nc\nd\n",
        ["tests/Lab.Tests/Existing.cs"] = "class Existing {}\n",
    }, Ct);

    private static string AddTest(string name) => $$"""
        diff --git a/tests/Lab.Tests/{{name}}.cs b/tests/Lab.Tests/{{name}}.cs
        new file mode 100644
        --- /dev/null
        +++ b/tests/Lab.Tests/{{name}}.cs
        @@ -0,0 +1 @@
        +class {{name}} {}

        """;

    [Fact]
    public async Task A_diff_is_applied_and_the_target_measured()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddTest("CalcTests"), "src/Lab/Calc.cs"));

        Assert.True(result.Green);
        Assert.Equal(["tests/Lab.Tests/CalcTests.cs", "tests/Lab.Tests/Existing.cs"], toolchain.SeenTestFiles.Single());
        Assert.Equal(50.0, result.TargetPct);
        Assert.Equal([[3, 4]], result.Uncovered);
        Assert.Contains("src/Lab/Calc.cs", result.CoberturaXml ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_diff_that_does_not_apply_runs_nothing()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);
        const string conflicting = """
            diff --git a/src/Lab/Calc.cs b/src/Lab/Calc.cs
            --- a/src/Lab/Calc.cs
            +++ b/src/Lab/Calc.cs
            @@ -1,1 +1,1 @@
            -not what is there
            +x

            """;

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", conflicting, "src/Lab/Calc.cs"));

        Assert.Equal((RunnerStatus.DiffRejected, BuildOutcome.Skipped), (result.Status, result.Build));
        Assert.Empty(toolchain.SeenTestFiles);
    }

    [Fact]
    public async Task Each_job_starts_from_a_clean_workspace()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);
        var commit = await repo.HeadAsync(Ct);

        await runner.RunAsync(new RunnerRequest(commit, "dotnet", AddTest("First"), null));
        await runner.RunAsync(new RunnerRequest(commit, "dotnet", AddTest("Second"), null));

        Assert.Equal(
            [["tests/Lab.Tests/Existing.cs", "tests/Lab.Tests/First.cs"], ["tests/Lab.Tests/Existing.cs", "tests/Lab.Tests/Second.cs"]],
            toolchain.SeenTestFiles);
    }

    [Fact]
    public async Task An_overrunning_job_is_reported_timed_out()
    {
        var repo = await RepoAsync();
        await using var runner = new Runner(repo, new FakeToolchain { Overrun = true });

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", null, "src/Lab/Calc.cs"));

        Assert.Equal(RunnerStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task A_hanging_process_is_stopped_at_the_time_limit()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var outcome = await ChildProcess.RunAsync("sleep", ["30"], Path.GetTempPath(), TimeSpan.FromMilliseconds(300), Ct);

        Assert.True(outcome.TimedOut);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_cancelled_process_is_stopped_with_its_children()
    {
        // A marker no other process carries, so the check below finds only what this test started.
        var marker = $"{31 + Random.Shared.Next(1000, 9000)}";
        using var stop = new CancellationTokenSource();
        var running = ChildProcess.RunAsync("sh", ["-c", $"sleep {marker} & sleep {marker}; wait"], Path.GetTempPath(),
            TimeSpan.FromMinutes(5), stop.Token);
        await Task.Delay(300, Ct);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await stop.CancelAsync();
        await running.ContinueWith(_ => { }, Ct);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
        var left = await ChildProcess.RunAsync("pgrep", ["-f", $"sleep {marker}"], Path.GetTempPath(), TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("", left.Output.Trim());
    }

    [Fact]
    public async Task A_job_cancelled_mid_build_stops_leaves_no_workspace_and_is_not_reused()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { Gate = new TaskCompletionSource() };
        await using var runner = new Runner(repo, toolchain);
        var client = await runner.ClientAsync();
        var commit = await repo.HeadAsync(Ct);
        var job = await (await client.PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet"), Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
        while (toolchain.SeenTestFiles.IsEmpty)
        {
            await Task.Delay(10, Ct);
        }

        var cancel = await client.PostAsync($"/runs/{job!.Id}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal(RunnerJobState.Canceled, (await cancel.Content.ReadFromJsonAsync<RunnerJob>(Json, Ct))!.State);
        await WaitUntilAsync(() => Directory.GetDirectories(runner.WorkRoot).Length == 0);
        Assert.Equal(RunnerJobState.Canceled, (await client.GetFromJsonAsync<RunnerJob>($"/runs/{job.Id}", Json, Ct))!.State);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/runs/{job.Id}/cancel", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/runs/job_nope/cancel", null, Ct)).StatusCode);

        // The same request again runs again: a cancelled run left nothing to reuse.
        toolchain.Gate = null;
        var again = await runner.RunAsync(new RunnerRequest(commit, "dotnet"));
        Assert.Null(again.ReusedFrom);
        Assert.Equal(2, toolchain.Plans.Count);
    }

    [Fact]
    public async Task A_shared_job_goes_on_for_the_caller_still_waiting()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { Gate = new TaskCompletionSource() };
        await using var runner = new Runner(repo, toolchain);
        var client = await runner.ClientAsync();
        var commit = await repo.HeadAsync(Ct);
        var first = await (await client.PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet"), Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
        while (toolchain.SeenTestFiles.IsEmpty)
        {
            await Task.Delay(10, Ct);
        }
        var second = await (await client.PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet"), Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);

        await client.PostAsync($"/runs/{first!.Id}/cancel", null, Ct);
        toolchain.Gate.SetResult();

        await WaitUntilAsync(async () => (await client.GetFromJsonAsync<RunnerJob>($"/runs/{second!.Id}", Json, Ct))!.State == RunnerJobState.Done);
        Assert.NotNull((await client.GetFromJsonAsync<RunnerJob>($"/runs/{second!.Id}", Json, Ct))!.Result);
        Assert.Equal(RunnerJobState.Canceled, (await client.GetFromJsonAsync<RunnerJob>($"/runs/{first.Id}", Json, Ct))!.State);
        Assert.Single(toolchain.Plans);
    }

    [Fact]
    public async Task A_caller_that_stops_cancels_its_job_on_the_runner()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { Gate = new TaskCompletionSource() };
        await using var runner = new Runner(repo, toolchain);
        var http = await runner.ClientAsync();
        var token = http.DefaultRequestHeaders.Authorization!.Parameter!;
        var seen = new List<RunnerJob>();
        using var stop = new CancellationTokenSource();
        var commit = await repo.HeadAsync(Ct);

        var waiting = new CoverageRunnerClient(http, _ => Task.FromResult(token), TimeSpan.FromMilliseconds(20))
            .RunAsync(new RunnerRequest(commit, "dotnet"), stop.Token, seen.Add);
        while (toolchain.SeenTestFiles.IsEmpty)
        {
            await Task.Delay(10, Ct);
        }
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        var id = seen[0].Id;
        Assert.Equal(RunnerJobState.Canceled, (await http.GetFromJsonAsync<RunnerJob>($"/runs/{id}", Json, Ct))!.State);
    }

    private static async Task WaitUntilAsync(Func<bool> condition) => await WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 200 && !await condition(); i++)
        {
            await Task.Delay(25, Ct);
        }
        Assert.True(await condition());
    }

    [Fact]
    public async Task Code_the_runner_starts_sees_no_secrets()
    {
        Environment.SetEnvironmentVariable("Auth__SigningKey_RunnerProbe", "signing-key-that-must-not-leak");
        Environment.SetEnvironmentVariable("OLLAMA_API_KEY_RunnerProbe", "ollama-key-that-must-not-leak");
        try
        {
            var outcome = await ChildProcess.RunAsync("env", [], Path.GetTempPath(), TimeSpan.FromSeconds(10), Ct);

            Assert.DoesNotContain("must-not-leak", outcome.Output);
            Assert.Contains("PATH=", outcome.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__SigningKey_RunnerProbe", null);
            Environment.SetEnvironmentVariable("OLLAMA_API_KEY_RunnerProbe", null);
        }
    }

    [Fact]
    public async Task A_second_job_waits_its_turn_and_says_where_it_is()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain { Gate = new TaskCompletionSource() };
        await using var runner = new Runner(repo, toolchain);
        var client = await runner.ClientAsync();
        var commit = await repo.HeadAsync(Ct);

        var first = await (await client.PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet"), Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
        while (toolchain.SeenTestFiles.IsEmpty)
        {
            await Task.Delay(10, Ct);
        }
        // A different request: an identical one would wait for the first instead of queueing (reuse).
        var second = await (await client.PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet", AddTest("Second")), Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
        var firstNow = await client.GetFromJsonAsync<RunnerJob>($"/runs/{first!.Id}", Json, Ct);
        toolchain.Gate.SetResult();

        Assert.Equal((RunnerJobState.Running, 0), (firstNow!.State, firstNow.QueuePosition));
        Assert.Equal((RunnerJobState.Queued, 1), (second!.State, second.QueuePosition));
    }

    [Fact]
    public async Task Only_a_caller_with_a_runner_token_gets_in()
    {
        var repo = await RepoAsync();
        await using var runner = new Runner(repo, new FakeToolchain());
        var commit = await repo.HeadAsync(Ct);

        var anonymous = await (await runner.ClientAsync(partner: null)).PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet"), Json, Ct);
        var stranger = await (await runner.ClientAsync(partner: "acme-portal")).PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet"), Json, Ct);
        var api = await (await runner.ClientAsync(partner: "maf-lab-assistant")).PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet"), Json, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, api.StatusCode);
    }

    [Theory]
    [InlineData("not-a-commit", "dotnet", null)]
    [InlineData("abcdef1", "maven", null)]
    [InlineData("abcdef1", "dotnet", "../etc/passwd")]
    public async Task A_malformed_job_is_refused(string commit, string toolchain, string? target)
    {
        var repo = await RepoAsync();
        await using var runner = new Runner(repo, new FakeToolchain());

        var response = await (await runner.ClientAsync()).PostAsJsonAsync("/runs", new RunnerRequest(commit, toolchain, null, target), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Dotnet_output_gives_diagnostics_counts_and_failures()
    {
        const string output = """
            /work/job/ws/tests/Lab.Tests/CalcTests.cs(4,51): error CS1002: ; expected [/work/job/ws/tests/Lab.Tests/Lab.Tests.csproj]
            Build failed with 1 error(s).
            """;

        var failed = DotnetToolchain.Parse(new ProcessOutcome(1, output, false), "/work/job/ws", "/out/r.xml");

        Assert.Equal(BuildOutcome.Failed, failed.Build);
        Assert.Equal(["tests/Lab.Tests/CalcTests.cs(4,51): error CS1002: ; expected"], failed.Diagnostics);
        Assert.Null(failed.CoberturaPath);

        const string ran = """
            failed Lab.Tests.CalcTests.Adds (12ms)
              Assert.Equal() Failure: Values differ
              Expected: 2
              Actual:   3

            Test run summary: Failed!
              total: 7
              failed: 1
              succeeded: 5
              skipped: 1
            """;

        var tested = DotnetToolchain.Parse(new ProcessOutcome(2, ran, false), "/work/job/ws", "/out/r.xml");

        Assert.Equal(BuildOutcome.Ok, tested.Build);
        Assert.Equal(new TestCounts(5, 1, 1), tested.Tests);
        Assert.Equal("Lab.Tests.CalcTests.Adds", tested.Failures.Single().Name);
        Assert.Contains("Expected: 2 Actual:   3", tested.Failures.Single().Message);
    }

    [Fact]
    public void A_dotnet_run_whose_summary_is_missing_is_not_green()
    {
        // The summary was lost (the output was cut short), but the failed tests had already been named.
        const string named = """
            failed Lab.Tests.CalcTests.Adds (12ms)
              Assert.Equal() Failure: Values differ
            failed Lab.Tests.CalcTests.Subtracts (3ms)
              Assert.Equal() Failure: Values differ
            """;
        var cut = DotnetToolchain.Parse(new ProcessOutcome(2, named, false), "/work/job/ws", "/out/r.xml");
        Assert.Equal((BuildOutcome.Ok, 2), (cut.Build, cut.Tests.Failed));

        // Nothing named and no summary: nothing says every test passed.
        var silent = DotnetToolchain.Parse(new ProcessOutcome(0, "Building...\n", false), "/work/job/ws", "/out/r.xml");
        Assert.Equal(1, silent.Tests.Failed);
        Assert.Contains("no summary", silent.Failures.Single().Message);
    }

    [Fact]
    public void Output_over_the_cap_keeps_its_beginning_and_its_end()
    {
        var output = new CappedOutput(1_000);
        output.Add("first line");
        for (var i = 0; i < 1_000; i++)
        {
            output.Add($"noise {i}");
        }
        output.Add("  failed: 4");

        var text = output.ToString();

        Assert.StartsWith("first line", text);
        Assert.EndsWith("  failed: 4" + Environment.NewLine, text);
        Assert.Contains("omitted", text);
        Assert.True(text.Length < 1_200);
    }

    [Fact]
    public void Vitest_json_gives_counts_failures_and_load_errors()
    {
        const string json = """
            {"numTotalTests":3,"numPassedTests":1,"numFailedTests":1,"numPendingTests":1,"numTodoTests":0,"success":false,
             "testResults":[
               {"name":"/work/job/ws/web/src/a.test.ts","status":"failed","message":"","assertionResults":[
                 {"fullName":"format pads","status":"failed","failureMessages":["AssertionError: expected '1' to be '01'\n    at /work/job/ws/web/src/a.test.ts:3:5"]},
                 {"fullName":"format ok","status":"passed","failureMessages":[]}]},
               {"name":"/work/job/ws/web/src/b.test.ts","status":"failed","message":"Transform failed: Unexpected token","assertionResults":[]}]}
            """;

        var outcome = VitestToolchain.Parse(new ProcessOutcome(1, "", false), json, "/work/job/ws", "/out/c.xml");

        Assert.Equal(BuildOutcome.Failed, outcome.Build);
        Assert.Equal(["web/src/b.test.ts: Transform failed: Unexpected token"], outcome.Diagnostics);
        Assert.Equal(new TestCounts(1, 1, 1), outcome.Tests);
        Assert.Equal(("format pads", "AssertionError: expected '1' to be '01'"), (outcome.Failures[0].Name, outcome.Failures[0].Message));
        Assert.Equal(["web/src/a.test.ts", "web/src/b.test.ts"], outcome.TestFiles);
    }

    // ── related tests (focus-test-runs-on-the-target) ───────────────────────────────────────────────────────────────

    /// <summary>A target that declares a type, a test file that uses it, and one that only mentions its name.</summary>
    private static Task<TempGitRepo> TypedRepoAsync() => TempGitRepo.CreateAsync(new Dictionary<string, string>
    {
        ["src/Lab/Calc.cs"] = "namespace Lab;\npublic class Calc\n{\n}\n",
        ["tests/Lab.Tests/UsesCalc.cs"] = "namespace Lab.Tests;\npublic class UsesCalc { private readonly Lab.Calc _c = new(); }\npublic class Box<T> { Calc? _c; }\n",
        ["tests/Lab.Tests/Unrelated.cs"] = "namespace Lab.Tests;\n// Calc, in a comment\npublic class Unrelated { private const string Name = \"Calc\"; }\n",
        ["tests/Lab.Tests/Fixture.cs"] = "namespace Lab.Tests;\npublic class Fixture { }\n",
        ["tests/Lab.Tests/UsesFixture.cs"] = "namespace Lab.Tests;\npublic class UsesFixture { private readonly Fixture _f = new(); }\n",
        ["tests/Lab.Tests/Lab.Tests.csproj"] = "<Project />\n",
    }, Ct);

    private static string NewFile(string path, string content) => $$"""
        diff --git a/{{path}} b/{{path}}
        new file mode 100644
        --- /dev/null
        +++ b/{{path}}
        @@ -0,0 +1 @@
        +{{content}}

        """;

    private static string AddCalcTests() =>
        NewFile("tests/Lab.Tests/CalcTests.cs", "namespace Lab.Tests { public class CalcTests { public class Nested { } } }");

    [Fact]
    public async Task Related_tests_are_the_changed_files_and_the_users_of_the_target()
    {
        var repo = await TypedRepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddCalcTests(), "src/Lab/Calc.cs", TestScope.Related));

        var plan = toolchain.Plans.Single();
        Assert.True(plan.Related);
        Assert.Equal(["Lab.Tests.Box`1", "Lab.Tests.CalcTests", "Lab.Tests.CalcTests+Nested", "Lab.Tests.UsesCalc"], plan.Filters);
        Assert.Equal((TestScope.Related, null), (result.Selection!.Scope, result.Selection.Reason));
        Assert.Equal(["tests/Lab.Tests/CalcTests.cs", "tests/Lab.Tests/UsesCalc.cs"], result.Selection.TestFiles);
        // The fake credits one line per test file it finds (all four lines here); the line hits come with the result.
        Assert.Equal([1, 2, 3, 4], result.TargetLines!.Covered);
        Assert.Empty(result.TargetLines.Uncovered);
    }

    [Fact]
    public async Task The_users_of_a_changed_helper_run_too()
    {
        var repo = await TypedRepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);
        const string helper = """
            diff --git a/tests/Lab.Tests/Fixture.cs b/tests/Lab.Tests/Fixture.cs
            --- a/tests/Lab.Tests/Fixture.cs
            +++ b/tests/Lab.Tests/Fixture.cs
            @@ -1,2 +1,2 @@
             namespace Lab.Tests;
            -public class Fixture { }
            +public class Fixture { public int Seed => 1; }

            """;

        await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", helper, "src/Lab/Calc.cs", TestScope.Related));

        Assert.Equal(["Lab.Tests.Box`1", "Lab.Tests.Fixture", "Lab.Tests.UsesCalc", "Lab.Tests.UsesFixture"], toolchain.Plans.Single().Filters);
    }

    [Fact]
    public async Task A_change_the_rule_cannot_follow_runs_the_whole_suite_and_says_why()
    {
        var repo = await TypedRepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);
        const string project = """
            diff --git a/tests/Lab.Tests/Lab.Tests.csproj b/tests/Lab.Tests/Lab.Tests.csproj
            --- a/tests/Lab.Tests/Lab.Tests.csproj
            +++ b/tests/Lab.Tests/Lab.Tests.csproj
            @@ -1 +1 @@
            -<Project />
            +<Project Sdk="x" />

            """;

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", project, "src/Lab/Calc.cs", TestScope.Related));

        Assert.False(toolchain.Plans.Single().Related);
        Assert.Equal(TestScope.All, result.Selection!.Scope);
        Assert.Contains("tests/Lab.Tests/Lab.Tests.csproj", result.Selection.Reason);
    }

    [Fact]
    public async Task Nothing_selected_runs_the_whole_suite()
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", null, "src/Lab/Calc.cs", TestScope.Related));

        Assert.False(toolchain.Plans.Single().Related);
        Assert.Equal((TestScope.All, RelatedTests.NothingSelected), (result.Selection!.Scope, result.Selection.Reason));
        Assert.Empty(result.Selection.TestFiles);
    }

    [Fact]
    public async Task Related_tests_that_hold_no_test_are_followed_by_the_whole_suite()
    {
        var repo = await TypedRepoAsync();
        var toolchain = new FakeToolchain { RelatedRunsNothing = true };
        await using var runner = new Runner(repo, toolchain);

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddCalcTests(), "src/Lab/Calc.cs", TestScope.Related));

        Assert.Equal([true, false], toolchain.Plans.Select(p => p.Related));
        Assert.Equal((TestScope.All, RelatedTests.NothingSelected), (result.Selection!.Scope, result.Selection.Reason));
        Assert.True(result.Green);
        Assert.True(result.Tests.Passed > 0);
    }

    [Fact]
    public async Task Without_a_scope_the_whole_suite_runs()
    {
        var repo = await TypedRepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);

        var result = await runner.RunAsync(new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", AddCalcTests(), "src/Lab/Calc.cs"));

        Assert.Same(TestPlan.Whole, toolchain.Plans.Single());
        Assert.Equal((TestScope.All, null), (result.Selection!.Scope, result.Selection.Reason));
    }

    [Theory]
    [InlineData("some", "src/Lab/Calc.cs")]
    [InlineData(TestScope.Related, null)]
    public async Task A_malformed_scope_is_refused(string tests, string? target)
    {
        var repo = await RepoAsync();
        var toolchain = new FakeToolchain();
        await using var runner = new Runner(repo, toolchain);

        var response = await (await runner.ClientAsync()).PostAsJsonAsync("/runs",
            new RunnerRequest(await repo.HeadAsync(Ct), "dotnet", null, target, tests), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(toolchain.Plans);
    }

    [Fact]
    public void Dotnet_arguments_filter_by_class_only_for_related_tests()
    {
        var options = new RunnerOptions();
        var related = new TestPlan(new TestSelection(TestScope.Related, ["tests/Maf.Lab.Tests/A.cs"]), ["Maf.Lab.Tests.A", "Maf.Lab.Tests.A+B"]);

        var whole = DotnetToolchain.Arguments(options, "/out/r.xml", TestPlan.Whole);
        var focused = DotnetToolchain.Arguments(options, "/out/r.xml", related);

        Assert.DoesNotContain("--filter-class", whole);
        Assert.Equal([.. whole, "--filter-class", "Maf.Lab.Tests.A", "--filter-class", "Maf.Lab.Tests.A+B"], focused);
        Assert.Equal(["test", "--project", "tests/Maf.Lab.Tests", "--", "--coverage"], whole.Take(5));
    }

    [Fact]
    public void Vitest_arguments_ask_for_related_tests_by_file()
    {
        var related = new TestPlan(new TestSelection(TestScope.Related, []), ["src/a.ts", "src/a.test.ts"]);

        var whole = VitestToolchain.Arguments("/out", "/out/v.json", TestPlan.Whole);
        var focused = VitestToolchain.Arguments("/out", "/out/v.json", related);

        Assert.Equal("run", whole[0]);
        Assert.Equal(["related", "--run", "--passWithNoTests", "src/a.ts", "src/a.test.ts", "--coverage"], focused.Take(6));
        Assert.Equal(whole.Skip(1), focused.Skip(5));
    }

    [Fact]
    public async Task Vitest_related_files_are_the_target_and_the_changed_files_under_web()
    {
        var repo = await TempGitRepo.CreateAsync(new Dictionary<string, string>
        {
            ["web/src/a.ts"] = "export const a = 1;\n",
            ["web/src/a.test.ts"] = "\n",
            ["web/src/test/render.tsx"] = "\n",
        }, Ct);
        var workspace = repo.Root;
        const string setup = "web/src/test/setup.ts";

        var plan = RelatedTests.Vitest(workspace, "web/src/a.ts", ["web/src/a.test.ts", "web/src/test/render.tsx", "web/src/gone.test.ts"], setup);
        var setupChanged = RelatedTests.Vitest(workspace, "web/src/a.ts", [setup], setup);
        var outside = RelatedTests.Vitest(workspace, "web/src/a.ts", ["web/package.json"], setup);

        Assert.True(plan.Related);
        Assert.Equal(["src/a.ts", "src/a.test.ts", "src/test/render.tsx"], plan.Filters);
        Assert.Contains(setup, setupChanged.Selection.Reason);
        Assert.Contains("web/package.json", outside.Selection.Reason);
        Assert.False(setupChanged.Related || outside.Related);
    }
}

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
    public TaskCompletionSource? Gate { get; set; }
    public bool Overrun { get; set; }
    /// <summary>The build warnings it reports, as a real build prints them.</summary>
    public IReadOnlyList<string>? Warnings { get; set; }

    public async Task<ToolchainOutcome> RunAsync(string workspace, string outputDir, TimeSpan timeLimit, CancellationToken ct)
    {
        var tests = Directory.Exists(Path.Combine(workspace, "tests"))
            ? Directory.GetFiles(Path.Combine(workspace, "tests"), "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(workspace, f)).Order().ToArray()
            : [];
        SeenTestFiles.Enqueue(tests);
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }
        if (Overrun)
        {
            return new ToolchainOutcome(BuildOutcome.Ok, [], new TestCounts(0, 0, 0), [], null, TimedOut: true);
        }
        var report = Path.Combine(outputDir, "dotnet.cobertura.xml");
        await File.WriteAllTextAsync(report,
            FakeCoverageRunner.Report(workspace, ("src/Lab/Calc.cs", Math.Min(4, tests.Length), 4)), ct);
        return new ToolchainOutcome(BuildOutcome.Ok, [], new TestCounts(tests.Length, 0, 0), [], report, false, Warnings);
    }
}

/// <summary>The coverage runner as its callers meet it (coverage-runner).</summary>
// Heavy (git, several hosts): run one after another rather than beside the timing-sensitive tests.
[Collection("TestGeneration")]
public sealed partial class CoverageRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Runner(TempGitRepo repo, FakeToolchain toolchain) : IAsyncDisposable
    {
        private WebApplication? _app;

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
                        ["Runner:WorkRoot"] = Directory.CreateTempSubdirectory("maf-runner-").FullName,
                        ["Runner:MaxConcurrent"] = "1",
                    });
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
        var second = await (await client.PostAsJsonAsync("/runs", new RunnerRequest(commit, "dotnet"), Json, Ct)).Content.ReadFromJsonAsync<RunnerJob>(Json, Ct);
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
    }
}

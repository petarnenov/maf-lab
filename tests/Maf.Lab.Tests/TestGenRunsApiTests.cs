using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// A run end to end inside one process: the api starts it over A2A on a real test agent, follows it, verifies it and
/// relays it to the browser (test-generation-runs).
/// </summary>
public sealed class TestGenRunsApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Target = "src/Lab/Calc.cs";

    private sealed class Stack(TempGitRepo repo, ApiFactory api, TestAgentFactory agent, FakeCoverageRunner runner, AttemptModel model)
        : IAsyncDisposable
    {
        public TempGitRepo Repo => repo;
        public ApiFactory Api => api;
        public TestAgentFactory Agent => agent;
        public FakeCoverageRunner Runner => runner;
        public AttemptModel Model => model;
        public HttpClient Admin { get; } = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

        public T Get<T>() where T : notnull => api.Services.GetRequiredService<T>();

        public async ValueTask DisposeAsync()
        {
            api.Dispose();
            await agent.DisposeAsync();
        }
    }

    private static string TestFile(int n) =>
        $"namespace Lab.Tests;\npublic class CalcTests\n{{\n    [Fact] public void Adds{n}() {{ Assert.Equal(2, Calc.Add(1, 1)); }}\n}}\n";

    private static async Task<Stack> StackAsync(Func<int, Move>? script = null, Func<RunnerRequest, RunnerResult>? runnerAnswer = null,
        bool agentUp = true, Dictionary<string, string?>? extra = null)
    {
        var repo = await TempGitRepo.CreateAsync(new Dictionary<string, string>
        {
            [Target] = "namespace Lab;\npublic static class Calc { public static int Add(int a, int b) => a + b; }\n",
        }, Ct);
        var runner = new FakeCoverageRunner
        {
            Answer = runnerAnswer ?? (r => r.Diff is null
                ? FakeCoverageRunner.Result(FakeCoverageRunner.Report("/work/job", (Target, 5, 10)), targetPct: 50)
                : FakeCoverageRunner.Result(FakeCoverageRunner.Report("/work/job", (Target, 9, 10)), targetPct: 90)),
        };
        var model = new AttemptModel(script ?? (n => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) })));
        var agent = new TestAgentFactory(repo, model, runner);
        var agentUrl = agentUp ? (await agent.ClientAsync(authenticated: false)).BaseAddress!.ToString() : "http://127.0.0.1:1/";
        var settings = new Dictionary<string, string?>
        {
            ["TestAgent:BaseUrl"] = agentUrl,
            ["TestAgent:ClientId"] = "maf-lab-assistant",
            ["TestAgent:ClientSecret"] = "assistant-secret",
            ["TestAgent:FollowerPollEvery"] = "00:00:00.100",
            ["TestAgent:ResubscribeAfter"] = "00:00:00.100",
            ["TestAgent:EventPollEvery"] = "00:00:00.050",
        };
        foreach (var (k, v) in extra ?? [])
        {
            settings[k] = v;
        }
        var api = CoverageApi.Create(repo, runner, settings);
        await CoverageApi.IngestAsync(api, await repo.HeadAsync(Ct), Toolchains.Dotnet, SnapshotKind.Official, null, (Target, 5, 10));
        return new Stack(repo, api, agent, runner, model);
    }

    private static Task<HttpResponseMessage> StartAsync(Stack s, int pct = 85, string model = "glm-5.3:cloud") =>
        s.Admin.PostAsJsonAsync("/api/coverage/runs", new CoverageEndpoints.StartRunRequest(Target, pct, model), Ct);

    private static async Task<RunSummary> UntilAsync(Stack s, string runId, Func<RunSummary, bool> done, int seconds = 30)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            var run = (await s.Admin.GetFromJsonAsync<RunDetail>($"/api/coverage/runs/{runId}", Json, Ct))!.Run;
            if (done(run) || DateTime.UtcNow > until)
            {
                return run;
            }
            await Task.Delay(50, Ct);
        }
    }

    private static async Task<int> ThresholdAsync(Stack s)
    {
        await using var db = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        return (await db.CoverageThresholds.FindAsync([Target], Ct))?.Pct ?? 80;
    }

    [Fact]
    public async Task A_run_goes_from_the_agent_to_a_verified_candidate()
    {
        // An attempt that takes a moment, as real ones take minutes: long enough for the follower to see it working.
        await using var s = await StackAsync(script: n => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) },
            Before: ct => Task.Delay(500, ct)));

        var response = await StartAsync(s);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var started = (await response.Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        Assert.Equal(TestGenRunState.Submitted, started.State);
        Assert.Equal(85, await ThresholdAsync(s));

        var run = await UntilAsync(s, started.Id, r => TestGenRunState.Final.Contains(r.State) || r.State == TestGenRunState.Candidate);
        Assert.Equal(TestGenRunState.Candidate, run.State);
        Assert.Equal(90.0, run.LastPct);
        Assert.StartsWith("test-agent/", run.Branch);

        // Every change was recorded in order, and the stream a browser opens now starts from where the run is.
        var events = await s.Get<TestGenRuns>().EventsAsync(started.Id, 0, Ct);
        Assert.Equal(TestGenRunState.Submitted, events[0].Run.State);
        Assert.Contains(events, e => e.Run.State == TestGenRunState.Working);
        Assert.Contains(events, e => e.Run.State == TestGenRunState.Verifying);
        Assert.Equal(TestGenRunState.Candidate, events[^1].Run.State);
    }

    [Fact]
    public async Task An_unreachable_agent_leaves_no_active_run_and_the_threshold_alone()
    {
        await using var s = await StackAsync(agentUp: false);

        var response = await StartAsync(s);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("agent_unavailable", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(80, await ThresholdAsync(s));
        await using var db = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var run = await db.TestGenRuns.SingleAsync(Ct);
        Assert.Equal((TestGenRunState.Failed, "agent_unavailable"), (run.State, run.Reason));
        Assert.Contains(await db.Audit.ToListAsync(Ct), a => a.ToolName == "a2a.testgen.start" && a.Outcome == "unreachable");
    }

    [Fact]
    public async Task A_model_off_the_allowlist_is_rejected_and_nothing_starts()
    {
        await using var s = await StackAsync();

        var response = await StartAsync(s, model: "llama9:cloud");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(80, await ThresholdAsync(s));
        Assert.Equal(0, s.Model.Calls);
    }

    [Fact]
    public async Task A_second_run_for_the_same_file_is_a_conflict()
    {
        var hold = new TaskCompletionSource();
        await using var s = await StackAsync(script: n => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) },
            Before: ct => hold.Task.WaitAsync(ct)));
        var first = await StartAsync(s);

        var second = await StartAsync(s, pct: 95);
        hold.TrySetResult();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("run_active", await second.Content.ReadAsStringAsync(Ct));
        Assert.Equal(85, await ThresholdAsync(s));
    }

    [Fact]
    public async Task A_raise_coverage_already_meets_needs_no_run()
    {
        await using var s = await StackAsync();

        var response = await StartAsync(s, pct: 40);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_mid_run_ends_it_canceled_with_no_branch()
    {
        var inAttempt = new TaskCompletionSource();
        await using var s = await StackAsync(script: n => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) },
            Before: async ct =>
            {
                inAttempt.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }));
        var started = (await (await StartAsync(s)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        await inAttempt.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        var response = await s.Admin.PostAsync($"/api/coverage/runs/{started.Id}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await UntilAsync(s, started.Id, r => r.State == TestGenRunState.Canceled);
        Assert.Equal(TestGenRunState.Canceled, run.State);
        Assert.Null(run.Branch);
        var task = await TestAgentFactory.RpcAsync(await s.Agent.ClientAsync(), "tasks/get", new { id = s.Agent.Tasks.Seen.Single() }, Ct);
        Assert.Equal("canceled", task.GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_late_subscriber_gets_the_current_state_first()
    {
        await using var s = await StackAsync();
        var started = (await (await StartAsync(s)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        await UntilAsync(s, started.Id, r => r.State == TestGenRunState.Candidate);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/coverage/runs/{started.Id}/events");
        var client = s.Api.ClientFor("bob", "firm-a", Role.ADVISOR);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        // A candidate is not final: the stream stays open for the decision still to come, so read its first event only.
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        var first = new List<string>();
        while (await reader.ReadLineAsync(Ct) is { } line && !(line.Length == 0 && first.Count > 0))
        {
            first.Add(line);
        }

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("event: snapshot", first[0]);
        Assert.Contains("\"state\":\"candidate\"", first[1]);
    }

    [Fact]
    public async Task A_run_past_its_deadline_is_cancelled_and_fails()
    {
        await using var s = await StackAsync(
            script: n => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) },
                Before: ct => Task.Delay(Timeout.Infinite, ct)),
            extra: new() { ["TestAgent:RunDeadline"] = "00:00:01" });
        var started = (await (await StartAsync(s)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;

        var run = await UntilAsync(s, started.Id, r => TestGenRunState.Final.Contains(r.State));

        Assert.Equal((TestGenRunState.Failed, "deadline"), (run.State, run.Reason));
    }

    [Fact]
    public async Task A_run_whose_follower_died_is_taken_over()
    {
        await using var s = await StackAsync();
        var started = (await (await StartAsync(s)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        await UntilAsync(s, started.Id, r => r.State == TestGenRunState.Candidate);
        // Replay the same task as if a replica that died had been following it, long ago.
        await using (var db = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct))
        {
            var run = await db.TestGenRuns.SingleAsync(Ct);
            db.TestGenRuns.Add(new TestGenRunRow
            {
                Id = "r_orphan", Path = "src/Lab/Other.cs", Toolchain = "dotnet", CommitSha = run.CommitSha, TargetPct = 85,
                Model = run.Model, TaskId = run.TaskId, State = TestGenRunState.Working, MaxAttempts = 5,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, CreatedBy = "alice",
                Follower = "dead-replica", FollowerHeartbeatAt = DateTime.UtcNow.AddMinutes(-10),
            });
            await db.SaveChangesAsync(Ct);
        }

        var orphan = await UntilAsync(s, "r_orphan", r => r.State != TestGenRunState.Working);

        // Followed to the end of its task by this replica: verified, whatever verification then found.
        Assert.NotEqual(TestGenRunState.Working, orphan.State);
        await using var check = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.NotEqual("dead-replica", (await check.TestGenRuns.SingleAsync(r => r.Id == "r_orphan", Ct)).Follower);
    }

    [Fact]
    public async Task Starting_and_cancelling_are_for_admins()
    {
        await using var s = await StackAsync();
        var advisor = s.Api.ClientFor("bob", "firm-a", Role.ADVISOR);

        var start = await advisor.PostAsJsonAsync("/api/coverage/runs", new CoverageEndpoints.StartRunRequest(Target, 85, "glm-5.3:cloud"), Ct);
        var cancel = await advisor.PostAsync("/api/coverage/runs/r_x/cancel", null, Ct);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (start.StatusCode, cancel.StatusCode));
    }

    [Fact]
    public async Task A_run_is_one_trace_across_the_api_the_agent_and_the_runner()
    {
        var spans = new ConcurrentQueue<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var repo = await TempGitRepo.CreateAsync(new Dictionary<string, string>
        {
            [Target] = "a\nb\nc\nd\n",
        }, Ct);
        // A real runner (its toolchain faked) behind a real socket, so the agent reaches it over HTTP.
        await using var runnerApp = await RealRunnerAsync(repo);
        var runnerUrl = new Uri(runnerApp.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First() + "/");
        var fake = new FakeCoverageRunner();
        var model = new AttemptModel(n => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) }));
        await using var agent = new TestAgentFactory(repo, model, fake, runnerUrl);
        var agentUrl = (await agent.ClientAsync(authenticated: false)).BaseAddress!.ToString();
        using var api = CoverageApi.Create(repo, fake, new Dictionary<string, string?>
        {
            ["TestAgent:BaseUrl"] = agentUrl,
            ["TestAgent:ClientSecret"] = "assistant-secret",
            ["TestAgent:FollowerPollEvery"] = "00:00:00.100",
        });
        await CoverageApi.IngestAsync(api, await repo.HeadAsync(Ct), Toolchains.Dotnet, SnapshotKind.Official, null, (Target, 1, 4));

        var response = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN)
            .PostAsJsonAsync("/api/coverage/runs", new CoverageEndpoints.StartRunRequest(Target, 85, "glm-5.3:cloud"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        System.Diagnostics.Activity? run = null;
        for (var i = 0; i < 300 && run is null; i++)
        {
            run = spans.FirstOrDefault(a => a.OperationName == "testgen.run");
            await Task.Delay(50, Ct);
        }

        Assert.NotNull(run);
        var trace = run.TraceId;
        var inTrace = spans.Where(a => a.TraceId == trace).ToList();
        Assert.Contains(inTrace, a => a.OperationName == "testgen.attempt");
        Assert.Contains(inTrace, a => a.OperationName == "runner.run");
        // The model calls are in it too, and its root is a request the api received: the one that started the run.
        Assert.Contains(inTrace, a => a.Source.Name == "Experimental.Microsoft.Extensions.AI" && a.OperationName == "chat");
        Assert.Contains(inTrace, a => a.Source.Name == "Microsoft.AspNetCore" && a.ParentSpanId == default);
        // Structure only: no source text, test code or prompt in any tag of the trace.
        var tags = string.Join("\n", inTrace.SelectMany(a => a.TagObjects).Select(t => $"{t.Key}={t.Value}"));
        Assert.DoesNotContain("Assert.Equal", tags);
        Assert.DoesNotContain("You write automated tests", tags);
    }

    private static async Task<Microsoft.AspNetCore.Builder.WebApplication> RealRunnerAsync(TempGitRepo repo)
    {
        var app = Maf.Lab.CoverageRunner.Program.BuildApp([], builder =>
        {
            Microsoft.AspNetCore.Hosting.HostingAbstractionsWebHostBuilderExtensions.UseUrls(builder.WebHost, "http://127.0.0.1:0");
            Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(builder.Configuration,
                new Dictionary<string, string?>
                {
                    ["Runner:RepoRoot"] = repo.Root,
                    ["Runner:WorkRoot"] = Directory.CreateTempSubdirectory("maf-runner-trace-").FullName,
                });
            builder.Services.AddSingleton<Maf.Lab.CoverageRunner.IToolchainRunner>(new FakeToolchain());
        });
        await app.StartAsync(Ct);
        return app;
    }
}

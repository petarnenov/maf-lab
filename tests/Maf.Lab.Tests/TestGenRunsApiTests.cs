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
// Heavy (git, several hosts): run one after another rather than beside the timing-sensitive tests.
[Collection("TestGeneration")]
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
        public HttpClient Admin { get; } = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);

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

    private static Task<HttpResponseMessage> StartAsync(Stack s, int pct = 85, string model = "glm-5.3:cloud", RunBudget? budget = null,
        RunLimitsInput? limits = null) =>
        s.Admin.PostAsJsonAsync("/api/coverage/runs", new CoverageEndpoints.StartRunRequest(Target, pct, model, budget, limits), Ct);

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

    /// <summary>A stack whose model uses 2M tokens (about $2 at the test price) per attempt and never reaches the target.</summary>
    private static Task<Stack> CostlyStackAsync() => StackAsync(
        script: n => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) }, Tokens: 2_000_000),
        runnerAnswer: r => FakeCoverageRunner.Result(FakeCoverageRunner.Report("/work/job", (Target, 5, 10)), targetPct: 50));

    [Fact]
    public async Task A_run_started_without_a_budget_is_unlimited()
    {
        await using var s = await CostlyStackAsync();

        var started = (await (await StartAsync(s)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        var run = await UntilAsync(s, started.Id, r => TestGenRunState.Final.Contains(r.State) || r.State == TestGenRunState.Candidate, 60);

        Assert.Equal(new RunBudget(null, null), started.Budget);
        // No cap was added on the way: every attempt ran, however much they cost.
        Assert.Equal((TestGenRequest.AttemptLimit, StopReason.Attempts), (run.Attempt, run.Reason));
    }

    [Fact]
    public async Task A_run_carries_exactly_the_budget_chosen_at_start()
    {
        await using var s = await CostlyStackAsync();

        var started = (await (await StartAsync(s, budget: new RunBudget(null, 0.5))).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        var run = await UntilAsync(s, started.Id, r => TestGenRunState.Final.Contains(r.State) || r.State == TestGenRunState.Candidate, 60);

        Assert.Equal(new RunBudget(null, 0.5), started.Budget);
        Assert.Equal(new RunBudget(null, 0.5), run.Budget);
        Assert.Equal(StopReason.Budget, run.Reason);
    }

    [Fact]
    public async Task A_run_carries_the_limits_chosen_at_start()
    {
        await using var s = await CostlyStackAsync();

        var started = (await (await StartAsync(s, limits: new RunLimitsInput(MaxAttempts: 3, ToolRoundsPerAttempt: 20)))
            .Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        var run = await UntilAsync(s, started.Id, r => TestGenRunState.Final.Contains(r.State) || r.State == TestGenRunState.Candidate, 60);

        Assert.Equal(new RunLimitsSummary(3, 20, 2), started.Limits);
        Assert.Equal((3, StopReason.Attempts), (run.Attempt, run.Reason));
    }

    [Fact]
    public async Task A_run_started_without_limits_has_the_defaults()
    {
        await using var s = await StackAsync();

        var started = (await (await StartAsync(s)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;

        Assert.Equal(new RunLimitsSummary(10, 40, 2, null, 3), started.Limits);
    }

    [Fact]
    public async Task A_run_keeps_its_deadline_and_bug_limit()
    {
        await using var s = await StackAsync();

        var started = (await (await StartAsync(s, limits: new RunLimitsInput(DeadlineMinutes: 30, MaxSuspectedBugs: 1)))
            .Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;

        Assert.Equal(new RunLimitsSummary(10, 40, 2, 30, 1), started.Limits);
    }

    [Theory]
    [InlineData(11, null, null, null, null)]
    [InlineData(null, 0, null, null, null)]
    [InlineData(null, 41, null, null, null)]
    [InlineData(null, null, 3, null, null)]
    [InlineData(null, null, null, 5, null)]
    [InlineData(null, null, null, 121, null)]
    [InlineData(null, null, null, null, 4)]
    public async Task A_limit_out_of_bounds_is_refused_and_nothing_changes(int? attempts, int? rounds, int? testRuns, int? deadline, int? bugs)
    {
        await using var s = await StackAsync();

        var response = await StartAsync(s, limits: new RunLimitsInput(attempts, rounds, testRuns, deadline, bugs));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(80, await ThresholdAsync(s));
        await using var db = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.False(await db.TestGenRuns.AnyAsync(Ct));
    }

    [Theory]
    [InlineData(0L, null)]
    [InlineData(null, -1.0)]
    public async Task A_budget_that_is_not_positive_is_refused_and_nothing_changes(long? tokens, double? cost)
    {
        await using var s = await StackAsync();

        var response = await StartAsync(s, budget: new RunBudget(tokens, cost));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(80, await ThresholdAsync(s));
        await using var db = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.False(await db.TestGenRuns.AnyAsync(Ct));
    }

    [Fact]
    public async Task A_run_that_changed_nothing_keeps_why_it_stopped()
    {
        // The model writes nothing, and the budget is gone after one attempt.
        await using var s = await StackAsync(script: _ => new Move(new Dictionary<string, string>()));

        var started = (await (await StartAsync(s, budget: new RunBudget(1_500, null))).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        var run = await UntilAsync(s, started.Id, r => TestGenRunState.Final.Contains(r.State));

        Assert.Equal((TestGenRunState.CompletedNoChange, StopReason.Budget), (run.State, run.Reason));
    }

    [Fact]
    public async Task A_run_whose_budget_is_spent_in_attempt_1_ends_at_attempt_1()
    {
        // Attempt 1 is expected to fit the cap; it uses 2M tokens, so the budget stops it before it finishes.
        await using var s = await CostlyStackAsync();

        var started = (await (await StartAsync(s, budget: new RunBudget(500_000, null))).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        var run = await UntilAsync(s, started.Id, r => TestGenRunState.Final.Contains(r.State) || r.State == TestGenRunState.Candidate, 60);

        Assert.Equal((1, StopReason.Budget), (run.Attempt, run.Reason));
    }

    [Fact]
    public async Task A_run_to_the_default_leaves_the_file_on_the_default()
    {
        await using var s = await StackAsync();

        var response = await StartAsync(s, pct: 80);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.Null(await db.CoverageThresholds.FindAsync([Target], Ct));
        Assert.Equal(80, await ThresholdAsync(s));
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
        Assert.Equal(StopReason.Target, run.Reason);
        Assert.Equal(90.0, run.LastPct);
        Assert.StartsWith("test-agent/", run.Branch);

        // Every change was recorded in order, and the stream a browser opens now starts from where the run is.
        var events = await s.Get<TestGenRuns>().EventsAsync(started.Id, 0, Ct);
        Assert.Equal(TestGenRunState.Submitted, events[0].Run.State);
        Assert.Contains(events, e => e.Run.State == TestGenRunState.Working);
        Assert.Contains(events, e => e.Run.State == TestGenRunState.Verifying);
        Assert.Equal(TestGenRunState.Candidate, events[^1].Run.State);

        // The work ended when the run became a candidate; a later decision does not move it (show-test-run-duration).
        var finished = await FinishedAtAsync(s, started.Id);
        Assert.NotNull(finished);
        Assert.True(finished >= run.CreatedAt);
        await s.Get<TestGenRuns>().FinishAsync(started.Id, TestGenRunState.Discarded, null, Ct);
        Assert.Equal(finished, await FinishedAtAsync(s, started.Id));
    }

    private static async Task<DateTime?> FinishedAtAsync(Stack s, string runId)
    {
        await using var db = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        return (await db.TestGenRuns.AsNoTracking().SingleAsync(r => r.Id == runId, Ct)).FinishedAt;
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

    /// <summary>A run's AG-UI stream, read one event at a time: the frame's name and its payload.</summary>
    private sealed class RunStream : IAsyncDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly StreamReader _reader;
        private readonly CancellationTokenSource _timeout;

        private RunStream(HttpResponseMessage response, StreamReader reader, CancellationTokenSource timeout) =>
            (_response, _reader, _timeout) = (response, reader, timeout);

        public string? MediaType => _response.Content.Headers.ContentType?.MediaType;

        public static async Task<RunStream> OpenAsync(HttpClient client, string runId, int seconds = 30)
        {
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            // A run is followed as an AG-UI agent on the run's own thread (agui-protocol-only).
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/coverage/runs/agent")
            {
                Content = JsonContent.Create(new { threadId = $"testgen:{runId}", runId, messages = Array.Empty<object>() }),
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return new RunStream(response, new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token)), timeout);
        }

        /// <summary>The next event, or null once the stream has closed.</summary>
        public async Task<(string Name, JsonElement Data)?> NextAsync()
        {
            string? name = null;
            while (await _reader.ReadLineAsync(_timeout.Token) is { } line)
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    name = line["event: ".Length..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var data = JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone();
                    // An AG-UI event names itself in its payload.
                    return (name ?? data.GetProperty("type").GetString()!, data);
                }
            }
            return null;
        }

        public async Task<List<(string Name, JsonElement Data)>> ReadAsync(Func<(string Name, JsonElement Data), bool>? until = null)
        {
            var events = new List<(string, JsonElement)>();
            while (await NextAsync() is { } e)
            {
                events.Add(e);
                if (until?.Invoke(e) == true)
                {
                    break;
                }
            }
            return events;
        }

        public ValueTask DisposeAsync()
        {
            _reader.Dispose();
            _response.Dispose();
            _timeout.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task A_finished_run_replays_as_AG_UI_the_same_on_any_replica()
    {
        await using var s = await StackAsync();
        var started = (await (await StartAsync(s)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        await UntilAsync(s, started.Id, r => r.State == TestGenRunState.Candidate);
        // A second replica over the same database: it has never followed this run.
        using var other = CoverageApi.Create(s.Repo, s.Runner, dataDir: s.Api.DataDir);

        await using var stream = await RunStream.OpenAsync(s.Api.ClientFor("bob", "firm-a", Role.USER), started.Id);
        var events = await stream.ReadAsync();
        await using var fromOther = await RunStream.OpenAsync(other.ClientFor("bob", "firm-a", Role.USER), started.Id);
        var otherEvents = await fromOther.ReadAsync();

        Assert.Equal("text/event-stream", stream.MediaType);
        Assert.Equal("RUN_STARTED", events[0].Name);
        Assert.Equal(($"testgen:{started.Id}", started.Id),
            (events[0].Data.GetProperty("threadId").GetString(), events[0].Data.GetProperty("runId").GetString()));
        Assert.Equal("STATE_SNAPSHOT", events[1].Name);
        Assert.Equal("candidate", events[1].Data.GetProperty("snapshot").GetProperty("state").GetString());
        Assert.Contains(events, e => e.Name == "STEP_STARTED" && e.Data.GetProperty("stepName").GetString() == "attempt 1: generating");
        var start = events.First(e => e.Name == "TOOL_CALL_START" && e.Data.GetProperty("toolCallName").GetString() == "write_file");
        var callId = start.Data.GetProperty("toolCallId").GetString();
        var result = events.Single(e => e.Name == "TOOL_CALL_RESULT" && e.Data.GetProperty("toolCallId").GetString() == callId);
        Assert.Contains("\"outcome\":\"ok\"", result.Data.GetProperty("content").GetString());
        Assert.Contains(events, e => e.Name == "TEXT_MESSAGE_CONTENT" && e.Data.GetProperty("delta").GetString() == "Done.");
        // An attempt's result is part of the run's state, never a custom event.
        Assert.Contains(events, e => e.Name == "STATE_SNAPSHOT" && e.Data.GetProperty("snapshot").GetProperty("attempts").GetArrayLength() > 0);
        Assert.DoesNotContain(events, e => e.Name == "CUSTOM");
        Assert.Equal("RUN_FINISHED", events[^1].Name);
        Assert.Single(events, e => e.Name is "RUN_FINISHED" or "RUN_ERROR");
        // Every replica tells the run the same way, from what the database holds.
        Assert.Equal(events.Select(e => e.Data.GetRawText()), otherEvents.Select(e => e.Data.GetRawText()));
    }

    [Fact]
    public async Task A_late_subscriber_gets_the_run_so_far_then_live_events()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var s = await StackAsync(script: n => new Move(
            new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) },
            Before: n == 2 ? ct => release.Task.WaitAsync(ct) : null));
        // 90% per attempt against a 95% target: attempt 2 starts, and waits there.
        var started = (await (await StartAsync(s, pct: 95)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        await UntilAsync(s, started.Id, r => r.Attempt == 2);

        await using var stream = await RunStream.OpenAsync(s.Api.ClientFor("bob", "firm-a", Role.USER), started.Id, seconds: 60);
        var sofar = await stream.ReadAsync(e => e.Name == "STEP_STARTED" && e.Data.GetProperty("stepName").GetString() == "attempt 2: generating");
        release.SetResult();
        var live = await stream.ReadAsync();

        Assert.Equal(["RUN_STARTED", "STATE_SNAPSHOT"], sofar.Take(2).Select(e => e.Name));
        Assert.Equal(2, sofar[1].Data.GetProperty("snapshot").GetProperty("attempt").GetInt32());
        // Attempt 1, whole, came before attempt 2 began.
        Assert.Contains(sofar, e => e.Name == "STATE_SNAPSHOT"
            && e.Data.GetProperty("snapshot").GetProperty("attempts").EnumerateArray().Any(a => a.GetProperty("attempt").GetInt32() == 1));
        Assert.Contains(live, e => e.Name == "STEP_STARTED" && e.Data.GetProperty("stepName").GetString() == "attempt 2: building");
        Assert.Contains(live, e => e.Name == "STATE_SNAPSHOT");
        Assert.Contains(live[^1].Name, new[] { "RUN_FINISHED", "RUN_ERROR" });
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
        await using var stream = await RunStream.OpenAsync(s.Admin, started.Id);
        var events = await stream.ReadAsync();
        // The run ends in the protocol's error; why is in its state, which says so before the end (agui-protocol-only).
        Assert.Equal("RUN_ERROR", events[^1].Name);
        var state = events.Last(e => e.Name == "STATE_SNAPSHOT").Data.GetProperty("snapshot");
        Assert.Equal((TestGenRunState.Failed, "deadline"), (state.GetProperty("state").GetString(), state.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task A_run_past_its_own_deadline_is_cancelled_and_fails()
    {
        await using var s = await StackAsync(
            script: n => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n) },
                Before: ct => Task.Delay(Timeout.Infinite, ct)));
        var started = (await (await StartAsync(s)).Content.ReadFromJsonAsync<RunSummary>(Json, Ct))!;
        await UntilAsync(s, started.Id, r => r.State == TestGenRunState.Working);
        // A run with a 30-minute deadline that started 31 minutes ago, under the configured 2 hours: its own deadline decides.
        await using (var db = await s.Get<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct))
        {
            var run = await db.TestGenRuns.SingleAsync(Ct);
            db.TestGenRuns.Add(new TestGenRunRow
            {
                Id = "r_short", Path = "src/Lab/Other.cs", Toolchain = "dotnet", CommitSha = run.CommitSha, TargetPct = 85,
                Model = run.Model, TaskId = run.TaskId, State = TestGenRunState.Working, MaxAttempts = 10,
                CreatedAt = DateTime.UtcNow.AddMinutes(-31), UpdatedAt = DateTime.UtcNow, CreatedBy = "alice",
                Follower = "dead-replica", FollowerHeartbeatAt = DateTime.UtcNow.AddMinutes(-10), DeadlineMinutes = 30,
            });
            await db.SaveChangesAsync(Ct);
        }

        var shortRun = await UntilAsync(s, "r_short", r => TestGenRunState.Final.Contains(r.State));

        Assert.Equal((TestGenRunState.Failed, "deadline"), (shortRun.State, shortRun.Reason));
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
        var advisor = s.Api.ClientFor("bob", "firm-a", Role.USER);

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

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN)
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
        var app = Maf.Lab.CoverageRunner.Program.BuildApp(ProjectDir.ContentRootArgs("Maf.Lab.CoverageRunner"), builder =>
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

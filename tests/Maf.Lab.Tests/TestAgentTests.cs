using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.A2A;
using Maf.Lab.TestAgent;
using Maf.Lab.TestGen;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

/// <summary>What the scripted model does in one attempt: files it writes, a bug it reports, how long it takes.</summary>
internal sealed record Move(
    IReadOnlyDictionary<string, string> Files,
    SuspectedBug? Bug = null,
    long Tokens = 1_000,
    Exception? Throw = null,
    Func<CancellationToken, Task>? Before = null,
    string Reply = "Done.",
    string? Reasoning = null);

/// <summary>
/// A model that, in each attempt, calls write_file (and report_suspected_bug) once and then says it is done. The
/// attempt number comes from the prompt, as a real model would read it.
/// </summary>
internal sealed partial class AttemptModel(Func<int, Move> script) : IChatClient
{
    public ConcurrentQueue<string> Prompts { get; } = new();
    public ConcurrentQueue<string> ToolResults { get; } = new();
    public int Calls;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        var list = messages.ToList();
        var prompt = list.Last(m => m.Role == ChatRole.User).Text;
        var attempt = int.Parse(AttemptNumber().Match(prompt).Groups[1].Value);
        var move = script(attempt);
        if (move.Throw is { } error)
        {
            throw error;
        }
        var usage = new UsageDetails { InputTokenCount = move.Tokens * 3 / 4, OutputTokenCount = move.Tokens / 4, TotalTokenCount = move.Tokens };

        if (list.Last().Contents.OfType<FunctionResultContent>().Any())
        {
            foreach (var result in list.Last().Contents.OfType<FunctionResultContent>())
            {
                ToolResults.Enqueue(result.Result?.ToString() ?? "");
            }
            List<AIContent> said = move.Reasoning is { } thought ? [new TextReasoningContent(thought), new TextContent(move.Reply)] : [new TextContent(move.Reply)];
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, said)) { Usage = usage };
        }

        Prompts.Enqueue(prompt);
        if (move.Before is { } before)
        {
            await before(cancellationToken);
        }
        var calls = move.Files.Select(f => (AIContent)new FunctionCallContent($"c{Guid.NewGuid():N}"[..12], "write_file",
            new Dictionary<string, object?> { ["path"] = f.Key, ["content"] = f.Value })).ToList();
        if (move.Bug is { } bug)
        {
            calls.Add(new FunctionCallContent($"c{Guid.NewGuid():N}"[..12], "report_suspected_bug", new Dictionary<string, object?>
            {
                ["testFile"] = bug.TestFile, ["test"] = bug.Test, ["title"] = bug.Title, ["description"] = bug.Description,
                ["expected"] = bug.Expected, ["actual"] = bug.Actual, ["failure"] = bug.Failure,
            }));
        }
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, calls)) { Usage = usage };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    [GeneratedRegex(@"This is attempt (\d+) of")]
    private static partial Regex AttemptNumber();
}

/// <summary>The test-generation agent as the api meets it (test-generation-agent).</summary>
// Heavy (git, several hosts): run one after another rather than beside the timing-sensitive tests.
[Collection("TestGeneration")]
public sealed class TestAgentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Marker = "SOURCE_LINE_ONLY_IN_THE_FILE";

    private static Task<TempGitRepo> RepoAsync() => TempGitRepo.CreateAsync(new Dictionary<string, string>
    {
        ["src/Lab/Calc.cs"] = $"namespace Lab;\npublic static class Calc\n{{\n    // {Marker}\n    public static int Add(int a, int b) => a + b;\n}}\n",
        ["tests/Lab.Tests/Existing.cs"] = "namespace Lab.Tests;\npublic class Existing { [Fact] public void Ok() { Assert.True(true); } }\n",
    }, Ct);

    private static string TestFile(int attempt) =>
        $"namespace Lab.Tests;\npublic class CalcTests\n{{\n    [Fact] public void Adds{attempt}() {{ Assert.Equal(2, Calc.Add(1, 1)); }}\n}}\n";

    private static Move Writes(int attempt) => new(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = TestFile(attempt) });

    /// <summary>The runner: the baseline (no diff) at 40%, every attempt at what <paramref name="pctFor"/> says.</summary>
    private static FakeCoverageRunner Runner(Func<int, double> pctFor)
    {
        var attempt = 0;
        return new FakeCoverageRunner
        {
            Answer = r => r.Diff is null
                ? FakeCoverageRunner.Result("<coverage/>", targetPct: 40) with { Uncovered = [[4, 5]] }
                : FakeCoverageRunner.Result("<coverage/>", targetPct: pctFor(Interlocked.Increment(ref attempt))) with { Uncovered = [[5, 5]] },
        };
    }

    [Fact]
    public async Task The_card_names_one_skill_and_what_it_is_not_for()
    {
        var repo = await RepoAsync();
        await using var agent = new TestAgentFactory(repo, new AttemptModel(Writes), Runner(_ => 90));

        var card = await (await agent.ClientAsync(authenticated: false)).GetFromJsonAsync<JsonElement>(AgentCardFactory.WellKnownPath, Ct);

        var skill = Assert.Single(card.GetProperty("skills").EnumerateArray());
        Assert.Equal(TestAgentCard.SkillId, skill.GetProperty("id").GetString());
        Assert.Contains("Not for", skill.GetProperty("description").GetString());
    }

    [Fact]
    public async Task A_task_without_a_token_is_refused()
    {
        var repo = await RepoAsync();
        await using var agent = new TestAgentFactory(repo, new AttemptModel(Writes), Runner(_ => 90));

        var response = await (await agent.ClientAsync(authenticated: false))
            .PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method = "message/send" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task It_stops_as_soon_as_an_attempt_reaches_the_target()
    {
        var repo = await RepoAsync();
        var model = new AttemptModel(Writes);
        var runner = Runner(n => n == 1 ? 70 : 86);
        await using var agent = new TestAgentFactory(repo, model, runner);

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        Assert.Equal("completed", task.GetProperty("status").GetProperty("state").GetString());
        var report = TestAgentFactory.Report(task);
        Assert.True(report.GoalReached);
        Assert.Equal(StopReason.Target, report.StopReason);
        Assert.Equal([1, 2], report.Attempts.Select(a => a.N));
        Assert.Equal((40.0, 86.0), (report.Baseline, report.Final));
        Assert.Equal((70.0, 86.0), (report.Attempts[1].Before, report.Attempts[1].After));
        Assert.Contains("+++ b/tests/Lab.Tests/CalcTests.cs", report.Diff);
        Assert.Contains("Adds2", report.Diff);
        // The second attempt was told what the first left uncovered.
        Assert.Contains("Lines still uncovered: 5", model.Prompts.ElementAt(1));
        // Baseline plus two measured attempts: the runner was never asked for a third.
        Assert.Equal(3, runner.Requests.Count);

        // What it did, as the run's activity shows it: numbered in order, each attempt's phases and result.
        var activity = TestAgentFactory.Activity(task);
        Assert.Equal(Enumerable.Range(1, activity.Count).Select(i => (long)i), activity.Select(e => e.Seq));
        // The baseline is measured first (attempt 0), so the run is never silent while its first build runs.
        Assert.Equal([(0, AttemptPhase.Measuring), (1, AttemptPhase.Generating), (1, AttemptPhase.Building), (1, AttemptPhase.Measuring),
            (2, AttemptPhase.Generating), (2, AttemptPhase.Building), (2, AttemptPhase.Measuring)],
            activity.Where(e => e.Type == ActivityType.Phase).Select(e => (e.Attempt, e.Phase)));
        Assert.Equal([(1, 40.0, 70.0), (2, 70.0, 86.0)],
            activity.Where(e => e.Type == ActivityType.Attempt).Select(e => (e.Attempt, e.Result!.Before!.Value, e.Result.After!.Value)));
        Assert.Contains(activity, e => e.Tool is { Name: "write_file", Path: "tests/Lab.Tests/CalcTests.cs", Outcome: ToolOutcome.Ok } t
            && t.Summary.StartsWith("created"));
        Assert.Contains(activity, e => e.Type == ActivityType.Text && e.Text == "Done.");
        // The timeline ends on why the agent stopped.
        Assert.Equal(new StoppedActivity(StopReason.Target, 2, 86), activity[^1].Stop);
        // A tool's summary is counts and sizes, never the file it wrote.
        Assert.DoesNotContain(activity, e => JsonSerializer.Serialize(e, TestGenKinds.Json).Contains("Calc.Add(1, 1)"));
    }

    [Fact]
    public async Task It_reports_the_goal_not_reached_after_five_attempts()
    {
        var repo = await RepoAsync();
        await using var agent = new TestAgentFactory(repo, new AttemptModel(Writes), Runner(n => 50 + n));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        var report = TestAgentFactory.Report(task);
        Assert.False(report.GoalReached);
        Assert.Equal(StopReason.Attempts, report.StopReason);
        Assert.Equal(5, report.Attempts.Count);
        Assert.Equal(55.0, report.Final);
        var last = TestAgentFactory.Activity(task)[^1];
        Assert.Equal((ActivityType.Stopped, new StoppedActivity(StopReason.Attempts, 5, 55)), (last.Type, last.Stop));
    }

    [Fact]
    public async Task A_task_may_ask_for_ten_attempts()
    {
        var repo = await RepoAsync();
        await using var agent = new TestAgentFactory(repo, new AttemptModel(Writes), Runner(n => 50 + n));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct), attempts: 10)), Ct);

        var report = TestAgentFactory.Report(task);
        Assert.Equal((StopReason.Attempts, 10), (report.StopReason, report.Attempts.Count));
    }

    [Fact]
    public async Task It_stops_before_an_attempt_that_would_cross_the_budget()
    {
        var repo = await RepoAsync();
        // Each attempt costs 200 000 tokens, above the ~130 000 estimated; the cap is 300 000, so the first starts and a
        // second, expected to cost what the first did, would cross it.
        var model = new AttemptModel(n => Writes(n) with { Tokens = 100_000 });
        await using var agent = new TestAgentFactory(repo, model, Runner(_ => 60));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct), maxTokens: 300_000)), Ct);

        var report = TestAgentFactory.Report(task);
        Assert.Equal((false, StopReason.Budget), (report.GoalReached, report.StopReason));
        Assert.Single(report.Attempts);
        Assert.Equal(200_000, report.Usage.InputTokens + report.Usage.OutputTokens);
        // The stop names the attempt it did not start.
        var last = TestAgentFactory.Activity(task)[^1];
        Assert.Equal((ActivityType.Stopped, 1, new StoppedActivity(StopReason.Budget, 1, 60, NotStarted: 2)), (last.Type, last.Attempt, last.Stop));
    }

    [Fact]
    public async Task An_attempt_that_wrote_nothing_is_told_so_in_the_next()
    {
        var repo = await RepoAsync();
        var model = new AttemptModel(n => n == 1 ? new Move(new Dictionary<string, string>()) : Writes(n));
        await using var agent = new TestAgentFactory(repo, model, Runner(n => n == 1 ? 40 : 86));

        await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        Assert.Contains("Attempt 1 wrote no test file", model.Prompts.ElementAt(1));
        Assert.Contains("tool rounds in this attempt", model.Prompts.First());
    }

    [Fact]
    public async Task A_warning_in_an_attempt_fails_it_and_is_fed_to_the_next()
    {
        const string ca2022 = "tests/Lab.Tests/CalcTests.cs(9,9): warning CA2022: Avoid inexact read with 'System.IO.Stream.Read(byte[], int, int)'";
        var repo = await RepoAsync();
        var model = new AttemptModel(Writes);
        var attempt = 0;
        var runner = new FakeCoverageRunner
        {
            // Attempt 1 reaches the target but warns, as the runner reports a diff's warnings: a failed build.
            Answer = r => r.Diff is null
                ? FakeCoverageRunner.Result("<coverage/>", targetPct: 40)
                : Interlocked.Increment(ref attempt) == 1
                    ? FakeCoverageRunner.Result("<coverage/>", targetPct: 90, build: BuildOutcome.Failed) with { Diagnostics = [ca2022] }
                    : FakeCoverageRunner.Result("<coverage/>", targetPct: 90),
        };
        await using var agent = new TestAgentFactory(repo, model, runner);

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        var report = TestAgentFactory.Report(task);
        // A test that warns is never the result, whatever it covers.
        Assert.Equal([BuildOutcome.Failed, BuildOutcome.Ok], report.Attempts.Select(a => a.Build));
        Assert.Equal(StopReason.Target, report.StopReason);
        Assert.Contains(ca2022, model.Prompts.ElementAt(1));
        Assert.Contains(Instructions.LintFailsTheBuild, model.Prompts.ElementAt(1));
    }

    [Fact]
    public async Task Without_caps_it_never_stops_for_the_budget()
    {
        var repo = await RepoAsync();
        // Far more per attempt than any configured cap used to allow.
        var model = new AttemptModel(n => Writes(n) with { Tokens = 2_000_000 });
        await using var agent = new TestAgentFactory(repo, model, Runner(n => 50 + n));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct), maxCost: null, maxTokens: null)), Ct);

        var report = TestAgentFactory.Report(task);
        Assert.Equal((StopReason.Attempts, 5), (report.StopReason, report.Attempts.Count));
    }

    [Fact]
    public async Task A_cancel_mid_run_stops_every_further_call()
    {
        var repo = await RepoAsync();
        var third = new TaskCompletionSource();
        var model = new AttemptModel(n => Writes(n) with
        {
            Before = async ct =>
            {
                if (n == 3)
                {
                    third.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
            },
        });
        var runner = Runner(_ => 50);
        await using var agent = new TestAgentFactory(repo, model, runner);
        var client = await agent.ClientAsync();

        var sending = TestAgentFactory.RpcAsync(client, "message/send", TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);
        await third.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var runnerCalls = runner.Requests.Count;
        var taskId = agent.Tasks.Seen.Single();
        await TestAgentFactory.RpcAsync(client, "tasks/cancel", new { id = taskId }, Ct);
        await sending.ContinueWith(_ => { }, Ct);

        var task = await TestAgentFactory.RpcAsync(client, "tasks/get", new { id = taskId }, Ct);
        Assert.Equal("canceled", task.GetProperty("status").GetProperty("state").GetString());
        Assert.Equal(runnerCalls, runner.Requests.Count);
        Assert.Equal(3, model.Prompts.Count);
    }

    [Fact]
    public async Task A_model_the_provider_refuses_fails_the_run()
    {
        var repo = await RepoAsync();
        var model = new AttemptModel(n => Writes(n) with
        {
            Throw = new HttpRequestException("this model is not included in your free usage", null, HttpStatusCode.Forbidden),
        });
        await using var agent = new TestAgentFactory(repo, model, Runner(_ => 50));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        var status = task.GetProperty("status");
        Assert.Equal("failed", status.GetProperty("state").GetString());
        Assert.Contains(TestGenFailure.ModelUnavailable, status.GetProperty("message").GetRawText());
        // A failed task ends with its error, not a stop entry.
        Assert.DoesNotContain(TestAgentFactory.Activity(task), e => e.Type == ActivityType.Stopped);
    }

    [Theory]
    [InlineData(TestGenRequest.AttemptLimit + 1, "src/Lab/Calc.cs")]
    [InlineData(5, "src/Lab/Missing.cs")]
    [InlineData(5, "tests/Lab.Tests/Existing.cs")]
    public async Task Invalid_input_is_rejected_before_any_model_call(int attempts, string target)
    {
        var repo = await RepoAsync();
        var model = new AttemptModel(Writes);
        var runner = Runner(_ => 90);
        await using var agent = new TestAgentFactory(repo, model, runner);

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct), attempts: attempts) with { TargetFile = target }), Ct);

        Assert.Equal("rejected", task.GetProperty("status").GetProperty("state").GetString());
        Assert.Equal(0, model.Calls);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task A_write_to_production_code_is_refused_and_the_run_goes_on()
    {
        var repo = await RepoAsync();
        var model = new AttemptModel(n => new Move(new Dictionary<string, string>
        {
            ["src/Lab/Calc.cs"] = "// replaced",
            ["tests/Lab.Tests/CalcTests.cs"] = TestFile(n),
        }));
        await using var agent = new TestAgentFactory(repo, model, Runner(_ => 90));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        var report = TestAgentFactory.Report(task);
        Assert.True(report.GoalReached);
        Assert.Contains(model.ToolResults, r => r.Contains("Refused: " + WorkspacePaths.WriteRefusal));
        Assert.DoesNotContain("src/Lab/Calc.cs", report.Diff);
        Assert.Contains(TestAgentFactory.Activity(task), e => e.Tool is { Name: "write_file", Path: "src/Lab/Calc.cs" } t
            && t.Outcome == ToolOutcome.Refused && t.Summary == WorkspacePaths.WriteRefusal);
    }

    [Fact]
    public async Task A_suspected_bug_is_skipped_and_reported_without_touching_the_code()
    {
        var repo = await RepoAsync();
        const string skipped = """
            namespace Lab.Tests;
            public class CalcBugTests
            {
                [Fact(Skip = "suspected-bug: Add overflows silently")] public void AddChecksOverflow() { Assert.Throws<OverflowException>(() => Calc.Add(int.MaxValue, 1)); }
                [Fact] public void Adds() { Assert.Equal(2, Calc.Add(1, 1)); }
            }
            """;
        var bug = new SuspectedBug("tests/Lab.Tests/CalcBugTests.cs", "AddChecksOverflow", "Add overflows silently",
            "Add is documented as checked arithmetic", "OverflowException", "-2147483648", "Assert.Throws() Failure: No exception was thrown");
        var model = new AttemptModel(_ => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcBugTests.cs"] = skipped }, bug));
        await using var agent = new TestAgentFactory(repo, model, Runner(_ => 90));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        var report = TestAgentFactory.Report(task);
        Assert.True(report.GoalReached);
        Assert.Equal(bug, Assert.Single(report.SuspectedBugs!));
        Assert.Contains("suspected-bug: Add overflows silently", report.Diff);
        Assert.DoesNotContain("src/Lab/", report.Diff);
        Assert.All(report.Attempts, a => Assert.Empty(a.GuardrailViolations));
    }

    [Fact]
    public async Task A_skip_that_is_not_reported_never_makes_the_result()
    {
        var repo = await RepoAsync();
        const string skipped = """
            namespace Lab.Tests;
            public class CalcTests { [Fact(Skip = "later")] public void Adds() { Assert.Equal(2, Calc.Add(1, 1)); } }
            """;
        var model = new AttemptModel(_ => new Move(new Dictionary<string, string> { ["tests/Lab.Tests/CalcTests.cs"] = skipped }));
        await using var agent = new TestAgentFactory(repo, model, Runner(_ => 90));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        var report = TestAgentFactory.Report(task);
        Assert.False(report.GoalReached);
        Assert.Equal("", report.Diff);
        Assert.All(report.Attempts, a => Assert.Contains(a.GuardrailViolations, v => v.Contains(TestGuardrails.Skipped)));
        // The next attempt was told which rule it broke.
        Assert.Contains(TestGuardrails.Skipped, string.Join("\n", model.Prompts.Skip(1)));
    }

    [Fact]
    public async Task No_prompt_or_source_reaches_logs_or_spans()
    {
        var repo = await RepoAsync();
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        await using var agent = new TestAgentFactory(repo, new AttemptModel(Writes), Runner(_ => 90));

        await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        var logs = string.Join("\n", agent.Logs.Messages);
        Assert.Contains("test run done", logs);
        var spanText = string.Join("\n", spans.SelectMany(s => s.TagObjects.Select(t => $"{t.Key}={t.Value}")
            .Concat(s.Events.SelectMany(e => e.Tags.Select(t => $"{t.Key}={t.Value}")))));
        // The key never passes through this code: it is read by ModelProviders (see ModelProvidersTests).
        foreach (var secret in new[] { Marker, "Assert.Equal(2, Calc.Add(1, 1))", "You write automated tests" })
        {
            Assert.DoesNotContain(secret, logs);
            Assert.DoesNotContain(secret, spanText);
        }
    }

    [Fact]
    public async Task The_models_text_reaches_the_activity_and_never_logs_or_spans()
    {
        const string said = "MODEL_TEXT_ONLY_IN_THE_ACTIVITY";
        const string thought = "REASONING_ONLY_IN_THE_ACTIVITY";
        var repo = await RepoAsync();
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        await using var agent = new TestAgentFactory(repo, new AttemptModel(n => Writes(n) with { Reply = said, Reasoning = thought }), Runner(_ => 90));

        var task = await TestAgentFactory.RpcAsync(await agent.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct))), Ct);

        var activity = TestAgentFactory.Activity(task);
        Assert.Contains(activity, e => e.Type == ActivityType.Text && e.Text == said);
        Assert.Contains(activity, e => e.Type == ActivityType.Reasoning && e.Text == thought);
        var summary = activity.First(e => e.Tool is { Name: "write_file" }).Tool!.Summary;
        var logs = string.Join("\n", agent.Logs.Messages);
        var spanText = string.Join("\n", spans.SelectMany(s => s.TagObjects.Select(t => $"{t.Key}={t.Value}")
            .Concat(s.Events.SelectMany(e => e.Tags.Select(t => $"{t.Key}={t.Value}")))));
        foreach (var content in new[] { said, thought, summary })
        {
            Assert.DoesNotContain(content, logs);
            Assert.DoesNotContain(content, spanText);
        }
    }

    private static async Task<(TestAgentTools Tools, Workspace Workspace)> ToolsAsync(TempGitRepo repo, FakeCoverageRunner? runner = null,
        int? testRuns = null, int? maxBugs = null)
    {
        var workspace = await Workspace.CreateAsync(repo.Root, Directory.CreateTempSubdirectory("maf-tools-").FullName, "t",
            await repo.HeadAsync(Ct), "dotnet", Ct);
        var client = new CoverageRunnerClient(new HttpClient(runner ?? Runner(_ => 77)) { BaseAddress = new Uri("http://runner.test/") },
            _ => Task.FromResult("t"), TimeSpan.FromMilliseconds(5));
        var request = TestAgentFactory.Request(await repo.HeadAsync(Ct)) with { TestRunsPerAttempt = testRuns, MaxSuspectedBugs = maxBugs };
        return (new TestAgentTools(workspace, request, client), workspace);
    }

    [Fact]
    public async Task A_written_test_can_be_read_back_and_is_in_the_diff()
    {
        var repo = await RepoAsync();
        var (tools, workspace) = await ToolsAsync(repo);
        await using var _ = workspace;

        var written = await tools.WriteFile("tests/Lab.Tests/CalcTests.cs", TestFile(1), Ct);
        var read = await tools.ReadFile("tests/Lab.Tests/CalcTests.cs", Ct);

        Assert.True(written.Created);
        Assert.Equal(TestFile(1).TrimEnd('\n'), read.Text);
        Assert.Contains("+++ b/tests/Lab.Tests/CalcTests.cs", await workspace.DiffAsync(Ct));
    }

    [Fact]
    public async Task A_long_file_is_read_in_part()
    {
        var repo = await RepoAsync();
        await repo.CommitAsync(new Dictionary<string, string> { ["src/Lab/Long.cs"] = CoverageApi.Lines(2_500) }, "long", Ct);
        var (tools, workspace) = await ToolsAsync(repo);
        await using var _ = workspace;

        var read = await tools.ReadFile("src/Lab/Long.cs", Ct);

        Assert.Equal((2_500, true), (read.Lines, read.Truncated));
        Assert.Equal(TestAgentTools.MaxReadLines, read.Text.Split('\n').Length);
    }

    [Fact]
    public async Task Listing_skips_git_internals()
    {
        var repo = await RepoAsync();
        var (tools, workspace) = await ToolsAsync(repo);
        await using var _ = workspace;

        var root = tools.ListFiles("");

        Assert.Equal(["src", "tests"], root.Entries.Select(e => e.Path));
    }

    [Fact]
    public async Task The_model_may_run_the_tests_twice_per_attempt()
    {
        var repo = await RepoAsync();
        var (tools, workspace) = await ToolsAsync(repo);
        await using var _ = workspace;
        tools.BeginAttempt();

        Assert.Equal(77, (await tools.RunTests(Ct)).TargetPct);
        await tools.RunTests(Ct);
        await Assert.ThrowsAsync<PathRefusedException>(() => tools.RunTests(Ct));
        Assert.Equal(77, tools.ReadCoverage("src/Lab/Calc.cs").Pct);
        Assert.Throws<PathRefusedException>(() => tools.ReadCoverage("src/Lab/Other.cs"));
    }

    [Fact]
    public async Task A_task_without_test_runs_refuses_the_first()
    {
        var repo = await RepoAsync();
        var (tools, workspace) = await ToolsAsync(repo, testRuns: 0);
        await using var _ = workspace;
        tools.BeginAttempt();

        await Assert.ThrowsAsync<PathRefusedException>(() => tools.RunTests(Ct));
    }

    [Fact]
    public async Task At_most_three_suspected_bugs_are_recorded()
    {
        var repo = await RepoAsync();
        var (tools, workspace) = await ToolsAsync(repo);
        await using var _ = workspace;

        var answers = Enumerable.Range(1, 4)
            .Select(i => tools.ReportSuspectedBug("tests/Lab.Tests/CalcTests.cs", $"T{i}", "t", "d", "e", "a", "f")).ToList();

        Assert.Equal([true, true, true, false], answers.Select(a => a.Recorded));
        Assert.Equal(3, tools.SuspectedBugs.Count);
    }

    [Fact]
    public async Task A_run_with_a_bug_limit_of_one_records_only_one()
    {
        var repo = await RepoAsync();
        var (tools, workspace) = await ToolsAsync(repo, maxBugs: 1);
        await using var _ = workspace;

        var answers = Enumerable.Range(1, 2)
            .Select(i => tools.ReportSuspectedBug("tests/Lab.Tests/CalcTests.cs", $"T{i}", "t", "d", "e", "a", "f")).ToList();

        Assert.Equal([true, false], answers.Select(a => a.Recorded));
        Assert.Contains("at most 1 suspected bug;", answers[1].Note);
    }
}

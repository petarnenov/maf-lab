using System.Text.Json;
using A2A;
using Maf.Lab.TestAgent;
using Maf.Lab.TestGen;

namespace Maf.Lab.Tests;

/// <summary>A task survives a restart of the agent (test-generation-agent: A task survives a restart of the agent).</summary>
[Collection("TestGeneration")]
public sealed class TestAgentRecoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<TempGitRepo> RepoAsync() => TempGitRepo.CreateAsync(new Dictionary<string, string>
    {
        ["src/Lab/Calc.cs"] = "namespace Lab;\npublic static class Calc\n{\n    public static int Add(int a, int b) => a + b;\n}\n",
        ["tests/Lab.Tests/Existing.cs"] = "namespace Lab.Tests;\npublic class Existing { [Fact] public void Ok() { Assert.True(true); } }\n",
    }, Ct);

    private static Move Writes(string file, int attempt) => new(new Dictionary<string, string>
    {
        [file] = $"namespace Lab.Tests;\npublic class T{attempt}\n{{\n    [Fact] public void Adds() {{ Assert.Equal(2, Calc.Add(1, 1)); }}\n}}\n",
    });

    /// <summary>The baseline at 40%, then each measured attempt at the next of <paramref name="pcts"/>.</summary>
    private static FakeCoverageRunner Runner(params double[] pcts)
    {
        var measured = 0;
        return new FakeCoverageRunner
        {
            Answer = r => r.Diff is null
                ? FakeCoverageRunner.Result("<coverage/>", targetPct: 40) with { Uncovered = [[4, 4]] }
                : FakeCoverageRunner.Result("<coverage/>", targetPct: pcts[Math.Min(Interlocked.Increment(ref measured), pcts.Length) - 1]) with { Uncovered = [[4, 4]] },
        };
    }

    private static async Task<AgentTask> EndedAsync(ITaskStore tasks, string taskId)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < until)
        {
            if (await tasks.GetTaskAsync(taskId, Ct) is { Status.State: not (TaskState.Submitted or TaskState.Working) } task)
            {
                return task;
            }
            await Task.Delay(50, Ct);
        }
        throw new TimeoutException($"task {taskId} did not end");
    }

    private static JsonElement Json(AgentTask task) => JsonSerializer.SerializeToElement(task, A2AJsonUtilities.DefaultOptions);

    private static AgentTask Stored(string id, TaskState state, DateTimeOffset at) => new()
    {
        Id = id,
        ContextId = "ctx",
        Status = new global::A2A.TaskStatus { State = state, Timestamp = at },
    };

    [Fact]
    public async Task A_restart_during_an_attempt_resumes_it_from_the_last_finished_one()
    {
        var repo = await RepoAsync();
        var runner = Runner(62, 90);
        var secondStarted = new TaskCompletionSource();
        var before = new AttemptModel(n => Writes("tests/Lab.Tests/FirstTests.cs", n) with
        {
            Before = async ct =>
            {
                if (n == 2)
                {
                    secondStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
            },
        });
        var first = new TestAgentFactory(repo, before, runner);
        var client = await first.ClientAsync();
        var sending = TestAgentFactory.RpcAsync(client, "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct), attempts: 10, maxCost: null, maxTokens: null)), Ct);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var taskId = first.Tasks.Seen.Single();
        var seqBefore = TestAgentFactory.Activity(Json((await first.Tasks.GetTaskAsync(taskId, Ct))!)).Max(e => e.Seq);

        // The process stops mid-attempt; a new one starts over the same store.
        await first.DisposeAsync();
        await sending.ContinueWith(_ => { }, Ct);
        Assert.Equal(TaskState.Working, (await first.Tasks.GetTaskAsync(taskId, Ct))!.Status!.State);
        var after = new AttemptModel(n => Writes("tests/Lab.Tests/SecondTests.cs", n));
        await using var second = new TestAgentFactory(repo, after, runner, tasks: first.Tasks, checkpoints: first.Checkpoints);
        await second.ClientAsync();

        var ended = await EndedAsync(second.Tasks, taskId);
        Assert.Equal(TaskState.Completed, ended.Status!.State);
        var task = Json(ended);
        var report = TestAgentFactory.Report(task);
        Assert.Equal(StopReason.Target, report.StopReason);
        Assert.Equal([1, 2], report.Attempts.Select(a => a.N));
        // The tests attempt 1 left are still there; attempt 2 added to them.
        Assert.Contains("FirstTests.cs", report.Diff);
        Assert.Contains("SecondTests.cs", report.Diff);
        // Attempt 1's tokens (two calls of 1 000) are still counted, beside attempt 2's.
        Assert.Equal(4_000, report.Usage.InputTokens + report.Usage.OutputTokens);
        Assert.Contains("This is attempt 2 of 10", Assert.Single(after.Prompts));
        // The baseline was not measured again.
        Assert.Single(runner.Requests, r => r.Diff is null);

        var activity = TestAgentFactory.Activity(task);
        var resumed = Assert.Single(activity, e => e.Type == ActivityType.Resumed);
        Assert.Equal(2, resumed.Attempt);
        Assert.True(resumed.Seq > seqBefore);
        Assert.Equal(activity.Count, activity.Select(e => e.Seq).Distinct().Count());
        // The checkpoint goes once the run has finished, just after its last update is stored.
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (await second.Checkpoints.GetAsync(taskId, Ct) is not null && DateTime.UtcNow < until)
        {
            await Task.Delay(20, Ct);
        }
        Assert.Null(await second.Checkpoints.GetAsync(taskId, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_resumed_run_runs_related_tests_only_when_it_kept_the_baseline_lines(bool kept)
    {
        var repo = await RepoAsync();
        var runner = new FakeCoverageRunner
        {
            Answer = r => r.Diff is null
                ? FakeCoverageRunner.Result("<coverage/>", targetPct: 40) with
                {
                    Uncovered = [[3, 5]],
                    Selection = TestSelection.Whole,
                    TargetLines = new LineHits([1, 2], [3, 4, 5]),
                }
                : FakeCoverageRunner.Result("<coverage/>", targetPct: 60) with
                {
                    Uncovered = [[5, 5]],
                    Selection = new TestSelection(r.Tests ?? TestScope.All, []),
                    TargetLines = new LineHits([3, 4], [1, 2, 5]),
                },
        };
        var secondStarted = new TaskCompletionSource();
        var before = new AttemptModel(n => Writes("tests/Lab.Tests/FirstTests.cs", n) with
        {
            Before = async ct =>
            {
                if (n == 2)
                {
                    secondStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
            },
        });
        var first = new TestAgentFactory(repo, before, runner);
        var sending = TestAgentFactory.RpcAsync(await first.ClientAsync(), "message/send",
            TestAgentFactory.Send(TestAgentFactory.Request(await repo.HeadAsync(Ct), attempts: 2, maxCost: null, maxTokens: null)), Ct);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var taskId = first.Tasks.Seen.Single();
        await first.DisposeAsync();
        await sending.ContinueWith(_ => { }, Ct);
        var saved = (await first.Checkpoints.GetAsync(taskId, Ct))!;
        Assert.Equal([1, 2], saved.Run!.BaselineLines!.Covered);
        if (!kept)
        {
            // As a checkpoint written before the baseline's lines were kept.
            await first.Checkpoints.SaveAsync(taskId, saved with { Run = saved.Run with { BaselineLines = null } }, Ct);
        }

        await using var second = new TestAgentFactory(repo, new AttemptModel(n => Writes("tests/Lab.Tests/SecondTests.cs", n)), runner,
            tasks: first.Tasks, checkpoints: first.Checkpoints);
        await second.ClientAsync();
        var ended = await EndedAsync(second.Tasks, taskId);

        Assert.Equal(TaskState.Completed, ended.Status!.State);
        // Attempt 1 before the restart ran the related tests; attempt 2 after it does so only with the lines kept.
        var attempts = runner.Requests.Where(r => r.Diff is not null).Select(r => r.Tests).ToList();
        Assert.Equal([TestScope.Related, kept ? TestScope.Related : TestScope.All], attempts);
        // Merged with the baseline (lines 1–4 of 5), or measured as it is.
        Assert.Equal(kept ? 80.0 : 60.0, TestAgentFactory.Report(Json(ended)).Attempts[^1].After);
    }

    [Fact]
    public async Task A_task_with_nothing_to_resume_from_ends_interrupted()
    {
        var repo = await RepoAsync();
        var tasks = new RecordingTaskStore();
        await tasks.SaveTaskAsync("t-lost", Stored("t-lost", TaskState.Working, DateTimeOffset.UtcNow.AddMinutes(-5)), Ct);
        var model = new AttemptModel(n => Writes("tests/Lab.Tests/T.cs", n));
        await using var agent = new TestAgentFactory(repo, model, Runner(90), tasks: tasks);
        await agent.ClientAsync();

        var task = await EndedAsync(tasks, "t-lost");
        Assert.Equal(TaskState.Failed, task.Status!.State);
        Assert.Contains(TestGenFailure.Interrupted, Json(task).GetProperty("status").GetProperty("message").GetRawText());
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task A_live_task_and_a_canceled_one_are_left_alone()
    {
        var repo = await RepoAsync();
        var commit = await repo.HeadAsync(Ct);
        var tasks = new RecordingTaskStore();
        var checkpoints = new InMemoryTaskCheckpointStore(TimeProvider.System);
        var request = TestAgentFactory.Request(commit);
        await tasks.SaveTaskAsync("t-live", Stored("t-live", TaskState.Working, DateTimeOffset.UtcNow.AddMinutes(-5)), Ct);
        await checkpoints.SaveAsync("t-live", new TaskCheckpoint(request), Ct);
        Assert.True(await checkpoints.TakeLeaseAsync("t-live", "another-replica", TimeSpan.FromMinutes(5), Ct));
        await tasks.SaveTaskAsync("t-canceled", Stored("t-canceled", TaskState.Canceled, DateTimeOffset.UtcNow.AddMinutes(-5)), Ct);
        await checkpoints.SaveAsync("t-canceled", new TaskCheckpoint(request), Ct);
        var model = new AttemptModel(n => Writes("tests/Lab.Tests/T.cs", n));
        var runner = Runner(90);
        await using var agent = new TestAgentFactory(repo, model, runner, tasks: tasks, checkpoints: checkpoints);
        await agent.ClientAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(800), Ct);

        Assert.Equal(TaskState.Working, (await tasks.GetTaskAsync("t-live", Ct))!.Status!.State);
        Assert.Equal(TaskState.Canceled, (await tasks.GetTaskAsync("t-canceled", Ct))!.Status!.State);
        Assert.Equal(0, model.Calls);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task A_lease_is_held_by_one_owner_at_a_time()
    {
        var checkpoints = new InMemoryTaskCheckpointStore(TimeProvider.System);

        Assert.True(await checkpoints.TakeLeaseAsync("t", "a", TimeSpan.FromMinutes(1), Ct));
        Assert.False(await checkpoints.TakeLeaseAsync("t", "b", TimeSpan.FromMinutes(1), Ct));
        Assert.False(await checkpoints.RenewLeaseAsync("t", "b", TimeSpan.FromMinutes(1), Ct));
        await checkpoints.ReleaseLeaseAsync("t", "b", Ct);
        Assert.True(await checkpoints.IsLeasedAsync("t", Ct));
        Assert.True(await checkpoints.RenewLeaseAsync("t", "a", TimeSpan.FromMinutes(1), Ct));
        await checkpoints.ReleaseLeaseAsync("t", "a", Ct);
        Assert.True(await checkpoints.TakeLeaseAsync("t", "b", TimeSpan.FromMinutes(1), Ct));
    }
}

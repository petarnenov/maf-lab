using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using A2A;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using MessageRole = A2A.Role;

namespace Maf.Lab.TestAgent;

/// <summary>
/// One run: up to the attempt cap of "the model writes tests, the runner builds and measures them". The loop is code —
/// the model does not decide when to stop, so it cannot talk its way past the attempt cap, the budget or a cancel.
/// The run belongs to its task, not to the connection that asked for it, nor to this process: while it runs, this
/// replica holds the task's lease and keeps a checkpoint of it in the shared store, so that after a restart the task
/// is taken over (<see cref="TaskRecovery"/>) and resumed from its last finished attempt.
/// </summary>
public sealed class TestGenerationHandler(
    IChatClientFactory models,
    CoverageRunnerClient runner,
    ITaskCheckpointStore checkpoints,
    ITaskStore tasks,
    ChannelEventNotifier notifier,
    IOptions<TestAgentOptions> options,
    IHostEnvironment environment,
    IHostApplicationLifetime lifetime,
    ILoggerFactory loggers) : IAgentHandler
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<string, bool> _canceled = new();
    private readonly ILogger _logger = loggers.CreateLogger<TestGenerationHandler>();

    /// <summary>This replica's name in a task's lease.</summary>
    public string Instance { get; } = $"{Maf.Lab.Hosting.InstanceIdentity.Name}-{Guid.NewGuid():N}";

    /// <summary>Whether this replica is running the task right now.</summary>
    public bool IsRunning(string taskId) => _running.ContainsKey(taskId);

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(queue, context.TaskId, context.ContextId);
        var request = Read(context);
        if (request?.Problem() is { } problem || request is null)
        {
            await updater.SubmitAsync(cancellationToken);
            await updater.RejectAsync(Say(request is null ? $"Send one {TestGenKinds.Request} data part." : request.Problem()!), cancellationToken);
            return;
        }

        // The lease and the request go to the store before the task is, so a recovery sweep never sees it unowned.
        if (!await checkpoints.TakeLeaseAsync(context.TaskId, Instance, options.Value.LeaseFor, cancellationToken))
        {
            _logger.LogWarning("new task already leased task={TaskId}", context.TaskId);
        }
        var checkpoint = new TaskCheckpoint(request);
        await SaveCheckpointAsync(context.TaskId, checkpoint);
        await updater.SubmitAsync(cancellationToken);
        await RunOwnedAsync(context.TaskId, checkpoint, updater, startAfter: 0, resumed: false);
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        _canceled[context.TaskId] = true;
        if (_running.TryGetValue(context.TaskId, out var cts))
        {
            await cts.CancelAsync();
        }
        await new TaskUpdater(queue, context.TaskId, context.ContextId).CancelAsync(cancellationToken);
        await ForgetAsync(context.TaskId);
    }

    /// <summary>
    /// Goes on with a task another process was running when it stopped, from its checkpoint. The caller holds the
    /// task's lease. No request is being served, so the task's updates are written to the store here, the way the
    /// SDK writes those of a task it was asked to run.
    /// </summary>
    public async Task ResumeAsync(string taskId, TaskCheckpoint checkpoint)
    {
        if (await tasks.GetTaskAsync(taskId, CancellationToken.None) is not { } task)
        {
            await checkpoints.ReleaseLeaseAsync(taskId, Instance, CancellationToken.None);
            return;
        }
        var startAfter = Math.Max(checkpoint.Run?.LastSeq ?? 0, LastSeqOf(task));
        _logger.LogInformation("resuming task={TaskId} attempt={Attempt}", taskId, checkpoint.Run?.NextAttempt ?? 0);
        await WithStoreQueueAsync(task, updater => RunOwnedAsync(taskId, checkpoint, updater, startAfter, resumed: true));
    }

    /// <summary>A task left running with nothing to resume it from ends, rather than staying "working" forever.</summary>
    public async Task FailInterruptedAsync(string taskId)
    {
        try
        {
            if (await tasks.GetTaskAsync(taskId, CancellationToken.None) is { } task)
            {
                _logger.LogWarning("task interrupted with no checkpoint task={TaskId}", taskId);
                await WithStoreQueueAsync(task, updater => updater.FailAsync(Say(TestGenFailure.Interrupted), CancellationToken.None).AsTask());
            }
        }
        finally
        {
            await checkpoints.ReleaseLeaseAsync(taskId, Instance, CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs the task while this replica holds its lease. A task that ends — done, failed, rejected or canceled — takes
    /// its checkpoint with it; one stopped by the host shutting down keeps it and gives its lease up, so the next start
    /// takes it over at once.
    /// </summary>
    private async Task RunOwnedAsync(string taskId, TaskCheckpoint checkpoint, TaskUpdater updater, long startAfter, bool resumed)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        _running[taskId] = cts;
        using var keeping = new CancellationTokenSource();
        var lease = KeepLeaseAsync(taskId, cts, keeping.Token);
        var ended = false;
        // One span for the run, under the caller's trace (propagated with the request); structure only, no content.
        using var span = Maf.Lab.Hosting.LabTelemetry.Source.StartActivity("testgen.run");
        span?.SetTag("testgen.toolchain", checkpoint.Request.Toolchain);
        span?.SetTag("testgen.target_pct", checkpoint.Request.TargetLinePct);
        span?.SetTag("testgen.max_attempts", checkpoint.Request.MaxAttempts);
        span?.SetTag("testgen.resumed", resumed);
        try
        {
            await RunAsync(taskId, checkpoint, updater, startAfter, resumed, cts.Token);
            ended = true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Canceled: CancelAsync has already said so. Shutting down or the lease lost: the task is taken over.
            ended = _canceled.ContainsKey(taskId);
            _logger.LogInformation("test run stopped task={TaskId} canceled={Canceled}", taskId, ended);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("test run failed task={TaskId} ({ErrorType})", taskId, ex.GetType().Name);
            ended = true;
            try
            {
                await updater.FailAsync(Say(TestGenFailure.Internal), CancellationToken.None);
            }
            catch (Exception failError) when (failError is not OperationCanceledException)
            {
                _logger.LogWarning("could not fail task={TaskId} ({ErrorType})", taskId, failError.GetType().Name);
            }
        }
        finally
        {
            await keeping.CancelAsync();
            await lease;
            _running.TryRemove(taskId, out _);
            _canceled.TryRemove(taskId, out _);
            if (ended)
            {
                await ForgetAsync(taskId);
            }
            else
            {
                try
                {
                    await checkpoints.ReleaseLeaseAsync(taskId, Instance, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("lease not released task={TaskId} ({ErrorType})", taskId, ex.GetType().Name);
                }
            }
        }
    }

    /// <summary>Renews the lease while the task runs; a replica that lost it stops, since another has taken over.</summary>
    private async Task KeepLeaseAsync(string taskId, CancellationTokenSource run, CancellationToken stop)
    {
        var ttl = options.Value.LeaseFor;
        using var timer = new PeriodicTimer(ttl / 3);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                bool held;
                try
                {
                    held = await checkpoints.RenewLeaseAsync(taskId, Instance, ttl, stop);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("lease renewal failed task={TaskId} ({ErrorType})", taskId, ex.GetType().Name);
                    continue;
                }
                if (!held)
                {
                    _logger.LogWarning("lease lost task={TaskId}", taskId);
                    await run.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    /// <summary>The task is over: its checkpoint and lease go.</summary>
    private async Task ForgetAsync(string taskId)
    {
        try
        {
            await checkpoints.DeleteAsync(taskId, CancellationToken.None);
            await checkpoints.ReleaseLeaseAsync(taskId, Instance, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("checkpoint not removed task={TaskId} ({ErrorType})", taskId, ex.GetType().Name);
        }
    }

    private async Task SaveCheckpointAsync(string taskId, TaskCheckpoint checkpoint)
    {
        // A checkpoint that cannot be written costs the resume, not the run.
        try
        {
            await checkpoints.SaveAsync(taskId, checkpoint, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("checkpoint not saved task={TaskId} ({ErrorType})", taskId, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> with an updater whose events are projected onto the stored task, under the SDK's
    /// per-task lock, with the SDK's own projection. An event for a task that has already ended is dropped: a cancel
    /// that landed first stays the task's end.
    /// </summary>
    private async Task WithStoreQueueAsync(AgentTask task, Func<TaskUpdater, Task> work)
    {
        var queue = new AgentEventQueue();
        var drain = Task.Run(async () =>
        {
            await foreach (var update in queue)
            {
                try
                {
                    using (await notifier.AcquireTaskLockAsync(task.Id, CancellationToken.None))
                    {
                        if (await tasks.GetTaskAsync(task.Id, CancellationToken.None) is not { } current || IsTerminal(current.Status?.State))
                        {
                            continue;
                        }
                        if (TaskProjection.Apply(current, update) is { } next)
                        {
                            await tasks.SaveTaskAsync(task.Id, next, CancellationToken.None);
                            notifier.Notify(task.Id, update);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("resumed task update not stored task={TaskId} ({ErrorType})", task.Id, ex.GetType().Name);
                }
            }
        });
        try
        {
            await work(new TaskUpdater(queue, task.Id, task.ContextId));
        }
        finally
        {
            queue.Complete();
            await drain;
        }
    }

    private static bool IsTerminal(TaskState? state) =>
        state is TaskState.Completed or TaskState.Canceled or TaskState.Failed or TaskState.Rejected;

    /// <summary>The highest activity sequence number the task holds.</summary>
    internal static long LastSeqOf(AgentTask task)
    {
        long last = 0;
        foreach (var artifact in task.Artifacts ?? [])
        {
            if (artifact.Name != TestGenKinds.ActivityArtifact)
            {
                continue;
            }
            foreach (var part in artifact.Parts ?? [])
            {
                if (part.Data is { ValueKind: JsonValueKind.Object } data && data.TryGetProperty("seq", out var seq)
                    && seq.TryGetInt64(out var n))
                {
                    last = Math.Max(last, n);
                }
            }
        }
        return last;
    }

    private async Task RunAsync(string taskId, TaskCheckpoint checkpoint, TaskUpdater updater, long startAfter, bool resumed,
        CancellationToken ct)
    {
        var request = checkpoint.Request;
        var from = checkpoint.Run;
        var opts = options.Value;
        var repoRoot = await RepoRootAsync(opts, ct);
        var workRoot = string.IsNullOrWhiteSpace(opts.WorkRoot) ? Path.Combine(Path.GetTempPath(), "maf-testgen") : opts.WorkRoot;

        Workspace workspace;
        try
        {
            workspace = await Workspace.CreateAsync(repoRoot, workRoot, taskId, request.Commit, request.Toolchain, ct);
        }
        catch (GitException ex)
        {
            _logger.LogWarning("checkout failed task={TaskId} git={Subcommand}", taskId, ex.Subcommand);
            await updater.FailAsync(Say(resumed ? TestGenFailure.Interrupted : TestGenFailure.CheckoutFailed), ct);
            return;
        }
        if (from is { WorkingDiff.Length: > 0 })
        {
            // The tests the finished attempts left, as the interrupted attempt found them.
            try
            {
                await workspace.ResetToAsync(from.WorkingDiff, ct);
            }
            catch (GitException ex)
            {
                _logger.LogWarning("resume could not restore tests task={TaskId} git={Subcommand}", taskId, ex.Subcommand);
                await workspace.DisposeAsync();
                await updater.FailAsync(Say(TestGenFailure.Interrupted), ct);
                return;
            }
        }

        await using (workspace)
        {
            if (!await workspace.CommitHasAsync(request.TargetFile, ct) || WorkspacePaths.IsWritable(request.TargetFile, request.Toolchain))
            {
                await updater.RejectAsync(Say("targetFile must be a production file of the repository at that commit."), ct);
                return;
            }

            var usage = new RunUsage(request.Budget, request.Price);
            var tools = new TestAgentTools(workspace, request, runner);
            // Each batch of activity is its own artifact: a resubscribing caller is told of new artifacts, not of
            // parts appended to one it has seen.
            var reporter = new ActivityReporter((entries, token) => updater.AddArtifactAsync(
                    entries.Select(a => new Part { Data = JsonSerializer.SerializeToElement(a, TestGenKinds.Json) }).ToList(),
                    artifactId: $"activity-{entries[0].Seq}", name: TestGenKinds.ActivityArtifact, lastChunk: true,
                    cancellationToken: token).AsTask(),
                TimeProvider.System, _logger, startAfter);
            var fileBytes = new FileInfo(Path.Combine(workspace.Root, request.TargetFile)).Length;
            var (estimatedInput, estimatedOutput) = AttemptEstimate.PerAttempt(fileBytes, request.ToolRounds);
            if (resumed)
            {
                reporter.Attempt = from?.NextAttempt ?? 0;
                await reporter.ResumedAsync(ct);
            }

            List<AttemptLog> attempts;
            double? baselinePct;
            Best best;
            double? current;
            string? feedback;
            // The workspace's diff is cumulative: an attempt that leaves it as it was wrote nothing.
            string previousDiff;
            long largestInput, largestOutput;
            int first;
            if (from is null)
            {
                // Where the file starts: the baseline every attempt is compared with. It runs the whole suite, so the
                // coverage other tests give the target is known, and attempts can run only the related tests. It takes
                // a whole build, so it is reported as it starts rather than leaving the run silent until the first attempt.
                await ProgressAsync(updater, reporter, request, 0, AttemptPhase.Measuring, null, usage, ct);
                RunnerResult baseline;
                try
                {
                    baseline = await runner.RunAsync(new RunnerRequest(request.Commit, request.Toolchain, null, request.TargetFile, TestScope.All), ct);
                }
                catch (RunnerUnavailableException)
                {
                    await updater.FailAsync(Say(TestGenFailure.RunnerUnavailable), ct);
                    return;
                }
                attempts = [];
                tools.BaselineLines = baseline.Measured ? baseline.TargetLines : null;
                baselinePct = baseline.TargetPct;
                best = new Best(baseline.TargetPct, "", []);
                current = baseline.TargetPct;
                feedback = baseline.Uncovered.Count > 0 ? Instructions.Feedback(baseline with { Failures = [] }, []) : null;
                previousDiff = "";
                (largestInput, largestOutput) = (0, 0);
                first = 1;
            }
            else
            {
                usage.Restore(from.Usage);
                tools.Restore(from.Bugs);
                tools.BaselineLines = from.BaselineLines;
                attempts = [.. from.Attempts];
                baselinePct = from.BaselinePct;
                best = new Best(from.BestPct, from.BestDiff, from.BestBugs);
                current = from.Current;
                feedback = from.Feedback;
                previousDiff = from.WorkingDiff;
                (largestInput, largestOutput) = (from.LargestInput, from.LargestOutput);
                first = from.NextAttempt;
            }
            var stop = StopReason.Attempts;
            int? notStarted = null;
            IChatClient chat = BuildChatClient(request.Model, request.ToolRounds, usage, reporter, out var nudge);

            // After the baseline and after every finished attempt: what a restart resumes from.
            Task CheckpointAsync(int next) => SaveCheckpointAsync(taskId, checkpoint with
            {
                Run = new RunCheckpoint(next, baselinePct, current, feedback, [.. attempts], best.Pct, best.Diff, [.. best.Bugs],
                    [.. tools.SuspectedBugs], previousDiff, largestInput, largestOutput, usage.Snapshot(), reporter.Seq,
                    tools.BaselineLines),
            });
            if (from is null)
            {
                await CheckpointAsync(1);
            }

            for (var n = first; n <= request.MaxAttempts; n++)
            {
                ct.ThrowIfCancellationRequested();
                // The next attempt is expected to cost what the estimate says, or what the dearest one so far did.
                if (usage.WouldExceed(Math.Max(estimatedInput, largestInput), Math.Max(estimatedOutput, largestOutput)))
                {
                    stop = StopReason.Budget;
                    notStarted = n;
                    break;
                }

                using var attemptSpan = Maf.Lab.Hosting.LabTelemetry.Source.StartActivity("testgen.attempt");
                attemptSpan?.SetTag("testgen.attempt", n);
                reporter.Attempt = n;
                await ProgressAsync(updater, reporter, request, n, AttemptPhase.Generating, current, usage, ct);
                tools.BeginAttempt();
                nudge.BeginAttempt();
                var (inputBefore, outputBefore) = (usage.InputTokens, usage.OutputTokens);
                var errors = new List<string>();
                try
                {
                    var agent = new ChatClientAgent(chat, new ChatClientAgentOptions
                    {
                        Name = "maf-lab-test-agent",
                        ChatOptions = new ChatOptions
                        {
                            Instructions = Instructions.System,
                            Tools = tools.All(),
                            Temperature = 0,
                        },
                    }, loggers);
                    // Streamed, so the model's text and reasoning reach the activity as they are written.
                    await foreach (var update in agent.RunStreamingAsync(Instructions.Attempt(request, n, current, feedback),
                        session: null, options: null, ct))
                    {
                        foreach (var content in update.Contents)
                        {
                            switch (content)
                            {
                                case TextReasoningContent reasoning when reasoning.Text is { Length: > 0 }:
                                    await reporter.TextAsync(ActivityType.Reasoning, reasoning.Text, ct);
                                    break;
                                case TextContent text when text.Text is { Length: > 0 }:
                                    await reporter.TextAsync(ActivityType.Text, text.Text, ct);
                                    break;
                            }
                        }
                    }
                    await reporter.EndTextAsync(ct);
                }
                catch (BudgetExceededException)
                {
                    stop = StopReason.Budget;
                    notStarted = n < request.MaxAttempts ? n + 1 : null;
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ProviderRefusal.Is(ex))
                {
                    _logger.LogWarning("model refused task={TaskId} model={Model} ({ErrorType})", taskId, request.Model, ex.GetType().Name);
                    await updater.FailAsync(Say(TestGenFailure.ModelUnavailable), ct);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A model call that failed for some other reason costs this attempt, not the run.
                    _logger.LogWarning("attempt model call failed task={TaskId} attempt={Attempt} ({ErrorType})", taskId, n, ex.GetType().Name);
                    errors.Add("The model call failed; this attempt made no further changes.");
                }

                largestInput = Math.Max(largestInput, usage.InputTokens - inputBefore);
                largestOutput = Math.Max(largestOutput, usage.OutputTokens - outputBefore);

                var diff = await workspace.DiffAsync(ct);
                var violations = TestGuardrails.Check(await workspace.ChangedFilesAsync(ct), tools.SuspectedBugs, request.SuspectedBugLimit);
                var forbidden = DiffPaths.Forbidden(diff, request.Toolchain);
                violations = [.. violations, .. forbidden.Select(p => new GuardrailViolation(p, "(file)", WorkspacePaths.WriteRefusal))];

                await ProgressAsync(updater, reporter, request, n, AttemptPhase.Building, current, usage, ct);
                RunnerResult result;
                try
                {
                    result = FocusedCoverage.Apply(
                        await runner.RunAsync(new RunnerRequest(request.Commit, request.Toolchain, diff, request.TargetFile, tools.Scope), ct),
                        tools.BaselineLines);
                    // The run would end here on the related tests alone: the whole suite decides, so a test this attempt
                    // breaks elsewhere is fed back now rather than found by verification, and the coverage is measured.
                    if (result.Focused && result.Green && violations.Count == 0 && diff.Length > 0 && result.TargetPct >= request.TargetLinePct)
                    {
                        await ProgressAsync(updater, reporter, request, n, AttemptPhase.Testing, current, usage, ct);
                        result = await runner.RunAsync(new RunnerRequest(request.Commit, request.Toolchain, diff, request.TargetFile, TestScope.All), ct);
                        attemptSpan?.SetTag("testgen.confirmed", true);
                    }
                }
                catch (RunnerUnavailableException)
                {
                    await updater.FailAsync(Say(TestGenFailure.RunnerUnavailable), ct);
                    return;
                }
                tools.Measured(result);
                attemptSpan?.SetTag("testgen.pct", result.TargetPct);
                attemptSpan?.SetTag("testgen.build", result.Build);
                attemptSpan?.SetTag("testgen.tests_failed", result.Tests.Failed);
                attemptSpan?.SetTag("testgen.violations", violations.Count);
                attemptSpan?.SetTag("testgen.tokens", usage.Tokens);
                attemptSpan?.SetTag("testgen.cost_usd", usage.CostUsd);

                var before = current;
                if (result.Measured)
                {
                    current = result.TargetPct;
                }
                var log = new AttemptLog(n, before, result.TargetPct, result.Build, result.Tests, TestsIn(diff),
                    [.. errors, .. result.Diagnostics.Take(10), .. result.Failures.Take(10).Select(f => $"{f.Name}: {f.Message}")],
                    violations.Select(v => v.ToString()).ToList(), result.Uncovered);
                attempts.Add(log);
                await reporter.AttemptAsync(
                    new AttemptActivity(log.Before, log.After, log.Build, log.Tests, log.Errors, log.GuardrailViolations.Count), ct);
                await ProgressAsync(updater, reporter, request, n, AttemptPhase.Measuring, current, usage, ct);

                // Only a clean attempt — it builds, every test passes, no rule is broken — can be the result.
                var clean = result.Green && violations.Count == 0 && diff.Length > 0;
                if (clean && (result.TargetPct ?? 0) >= (best.Pct ?? -1))
                {
                    best = new Best(result.TargetPct, diff, tools.SuspectedBugs.ToList());
                }
                if (clean && result.TargetPct >= request.TargetLinePct)
                {
                    stop = StopReason.Target;
                    break;
                }
                feedback = Instructions.Feedback(result, violations, diff == previousDiff ? n : null);
                previousDiff = diff;
                await CheckpointAsync(n + 1);
            }

            if (Encoding.UTF8.GetByteCount(best.Diff) > TestGenFailure.MaxDiffBytes)
            {
                await updater.FailAsync(Say(TestGenFailure.DiffTooLarge), ct);
                return;
            }

            // The timeline ends on why the work stopped, not on the last open phase.
            await reporter.StoppedAsync(new StoppedActivity(stop, reporter.Attempt, best.Pct, notStarted), ct);
            var report = new TestGenReport(TestGenKinds.Report, stop == StopReason.Target, stop, request.TargetLinePct,
                baselinePct, best.Pct, attempts, usage.Snapshot(), best.Diff, best.Bugs);
            await updater.AddArtifactAsync(
                [new Part { Data = JsonSerializer.SerializeToElement(report, TestGenKinds.Json) }],
                name: TestGenKinds.ReportArtifact, cancellationToken: ct);
            System.Diagnostics.Activity.Current?.SetTag("testgen.stop", stop);
            _logger.LogInformation("test run done task={TaskId} stop={Stop} attempts={Attempts} final={Final} tokens={Tokens}",
                taskId, stop, attempts.Count, best.Pct, usage.Tokens);
            await updater.CompleteAsync(Say(stop == StopReason.Target
                ? $"Reached {best.Pct:0.0}% of {request.TargetLinePct}%."
                : $"Stopped ({stop}) at {best.Pct?.ToString("0.0") ?? "?"}% of {request.TargetLinePct}%."), ct);
        }
    }

    private sealed record Best(double? Pct, string Diff, IReadOnlyList<SuspectedBug> Bugs);

    /// <summary>
    /// provider → usage counting → round nudge → the tool loop. The counter sits on the provider so every call the loop
    /// makes is counted; the nudge tells the model to write when few rounds remain; the tool loop has a round cap and
    /// turns a refused tool call into text the model can act on.
    /// </summary>
    private IChatClient BuildChatClient(string model, int toolRounds, RunUsage usage, ActivityReporter reporter,
        out RoundNudgeChatClient nudge)
    {
        var provider = new OpenTelemetryChatClient(models.CreateChatClient(model));
        var budgeted = new BudgetedChatClient(provider, usage);
        nudge = new RoundNudgeChatClient(budgeted, toolRounds);
        return new FunctionInvokingChatClient(nudge, loggers)
        {
            MaximumIterationsPerRequest = toolRounds,
            FunctionInvoker = async (context, ct) =>
            {
                object? result;
                var (outcome, problem) = (ToolOutcome.Ok, (string?)null);
                try
                {
                    result = await context.Function.InvokeAsync(context.Arguments, ct);
                }
                catch (PathRefusedException ex)
                {
                    (outcome, problem) = (ToolOutcome.Refused, ex.Message);
                    result = $"Refused: {ex.Message}";
                }
                catch (RunnerUnavailableException)
                {
                    (outcome, problem) = (ToolOutcome.Failed, "The test runner is unavailable right now.");
                    result = problem;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("tool {Tool} failed ({ErrorType})", context.Function.Name, ex.GetType().Name);
                    (outcome, problem) = (ToolOutcome.Failed, "The tool failed.");
                    result = problem;
                }
                await reporter.ToolAsync(ToolSummaries.Of(context.Function.Name, context.Arguments, result, outcome, problem), ct);
                return result;
            },
        };
    }

    private static async Task ProgressAsync(TaskUpdater updater, ActivityReporter reporter, TestGenRequest request, int attempt,
        string phase, double? pct, RunUsage usage, CancellationToken ct)
    {
        await reporter.PhaseAsync(phase, ct);
        await updater.StartWorkAsync(new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = MessageRole.Agent,
            Parts =
            [
                new Part { Text = $"Attempt {attempt} of {request.MaxAttempts}: {phase}." },
                new Part
                {
                    Data = JsonSerializer.SerializeToElement(
                        new TestGenProgress(TestGenKinds.Progress, attempt, request.MaxAttempts, phase, pct, usage.Tokens, usage.CostUsd),
                        TestGenKinds.Json),
                },
            ],
        }, ct);
    }

    /// <summary>The request's data part, or null when there is none of the right kind.</summary>
    internal static TestGenRequest? Read(RequestContext context)
    {
        foreach (var part in context.Message?.Parts ?? [])
        {
            if (part.Data is { ValueKind: JsonValueKind.Object } data
                && data.TryGetProperty("kind", out var kind) && kind.GetString() == TestGenKinds.Request)
            {
                try
                {
                    return data.Deserialize<TestGenRequest>(TestGenKinds.Json);
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }
        return null;
    }

    /// <summary>The files the diff adds or changes, as the report's "tests added".</summary>
    private static IReadOnlyList<string> TestsIn(string diff) =>
        DiffPaths.Of(diff).Where(p => p != "/dev/null").Distinct().ToList();

    private async Task<string> RepoRootAsync(TestAgentOptions opts, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(opts.RepoRoot)
            ? opts.RepoRoot
            : (await Git.CheckedAsync(environment.ContentRootPath, ["rev-parse", "--show-toplevel"], ct)).Text.Trim();

    private static Message Say(string text) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = MessageRole.Agent,
        Parts = [new Part { Text = text }],
    };
}

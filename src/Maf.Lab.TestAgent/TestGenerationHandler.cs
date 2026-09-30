using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using A2A;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestGen;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using MessageRole = A2A.Role;

namespace Maf.Lab.TestAgent;

/// <summary>
/// One run: up to five attempts of "the model writes tests, the runner builds and measures them". The loop is code —
/// the model does not decide when to stop, so it cannot talk its way past the attempt cap, the budget or a cancel.
/// The run belongs to its task, not to the connection that asked for it: it stops for a cancel, for the host
/// shutting down, and for nothing else.
/// </summary>
public sealed class TestGenerationHandler(
    IChatClientFactory models,
    CoverageRunnerClient runner,
    IOptions<TestAgentOptions> options,
    IHostEnvironment environment,
    IHostApplicationLifetime lifetime,
    ILoggerFactory loggers) : IAgentHandler
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    private readonly ILogger _logger = loggers.CreateLogger<TestGenerationHandler>();

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

        await updater.SubmitAsync(cancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        _running[context.TaskId] = cts;
        var work = cts.Token;
        // One span for the run, under the caller's trace (propagated with the request); structure only, no content.
        using var span = Maf.Lab.Hosting.LabTelemetry.Source.StartActivity("testgen.run");
        span?.SetTag("testgen.toolchain", request.Toolchain);
        span?.SetTag("testgen.target_pct", request.TargetLinePct);
        span?.SetTag("testgen.max_attempts", request.MaxAttempts);
        try
        {
            await RunAsync(context.TaskId, request, updater, work);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            // Canceled: CancelAsync has already said so. Shutting down: the caller's deadline will notice.
            _logger.LogInformation("test run stopped task={TaskId}", context.TaskId);
        }
        finally
        {
            _running.TryRemove(context.TaskId, out _);
        }
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue queue, CancellationToken cancellationToken)
    {
        if (_running.TryGetValue(context.TaskId, out var cts))
        {
            await cts.CancelAsync();
        }
        await new TaskUpdater(queue, context.TaskId, context.ContextId).CancelAsync(cancellationToken);
    }

    private async Task RunAsync(string taskId, TestGenRequest request, TaskUpdater updater, CancellationToken ct)
    {
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
            await updater.FailAsync(Say(TestGenFailure.CheckoutFailed), ct);
            return;
        }

        await using (workspace)
        {
            if (!await workspace.CommitHasAsync(request.TargetFile, ct) || WorkspacePaths.IsWritable(request.TargetFile, request.Toolchain))
            {
                await updater.RejectAsync(Say("targetFile must be a production file of the repository at that commit."), ct);
                return;
            }

            var usage = new RunUsage(request.Budget, request.Price);
            var tools = new TestAgentTools(workspace, request, runner, opts);
            var attempts = new List<AttemptLog>();
            var fileBytes = new FileInfo(Path.Combine(workspace.Root, request.TargetFile)).Length;
            var (estimatedInput, estimatedOutput) = AttemptEstimate.PerAttempt(fileBytes);

            // Where the file starts: the baseline every attempt is compared with.
            RunnerResult baseline;
            try
            {
                baseline = await runner.RunAsync(new RunnerRequest(request.Commit, request.Toolchain, null, request.TargetFile), ct);
            }
            catch (RunnerUnavailableException)
            {
                await updater.FailAsync(Say(TestGenFailure.RunnerUnavailable), ct);
                return;
            }

            var best = new Best(baseline.TargetPct, "", []);
            var current = baseline.TargetPct;
            string? feedback = baseline.Uncovered.Count > 0 ? Instructions.Feedback(baseline with { Failures = [] }, []) : null;
            var stop = StopReason.Attempts;
            long largestInput = 0, largestOutput = 0;
            IChatClient chat = BuildChatClient(request.Model, usage, opts);

            for (var n = 1; n <= request.MaxAttempts; n++)
            {
                ct.ThrowIfCancellationRequested();
                // The next attempt is expected to cost what the estimate says, or what the dearest one so far did.
                if (usage.WouldExceed(Math.Max(estimatedInput, largestInput), Math.Max(estimatedOutput, largestOutput)))
                {
                    stop = StopReason.Budget;
                    break;
                }

                using var attemptSpan = Maf.Lab.Hosting.LabTelemetry.Source.StartActivity("testgen.attempt");
                attemptSpan?.SetTag("testgen.attempt", n);
                await ProgressAsync(updater, request, n, AttemptPhase.Generating, current, usage, ct);
                tools.BeginAttempt();
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
                    await agent.RunAsync(Instructions.Attempt(request, n, current, feedback), session: null, options: null, ct);
                }
                catch (BudgetExceededException)
                {
                    stop = StopReason.Budget;
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
                var violations = TestGuardrails.Check(await workspace.ChangedFilesAsync(ct), tools.SuspectedBugs);
                var forbidden = DiffPaths.Forbidden(diff, request.Toolchain);
                violations = [.. violations, .. forbidden.Select(p => new GuardrailViolation(p, "(file)", WorkspacePaths.WriteRefusal))];

                await ProgressAsync(updater, request, n, AttemptPhase.Building, current, usage, ct);
                RunnerResult result;
                try
                {
                    result = await runner.RunAsync(new RunnerRequest(request.Commit, request.Toolchain, diff, request.TargetFile), ct);
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
                attempts.Add(new AttemptLog(n, before, result.TargetPct, result.Build, result.Tests, TestsIn(diff),
                    [.. errors, .. result.Diagnostics.Take(10), .. result.Failures.Take(10).Select(f => $"{f.Name}: {f.Message}")],
                    violations.Select(v => v.ToString()).ToList(), result.Uncovered));
                await ProgressAsync(updater, request, n, AttemptPhase.Measuring, current, usage, ct);

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
                feedback = Instructions.Feedback(result, violations);
            }

            if (Encoding.UTF8.GetByteCount(best.Diff) > TestGenFailure.MaxDiffBytes)
            {
                await updater.FailAsync(Say(TestGenFailure.DiffTooLarge), ct);
                return;
            }

            var report = new TestGenReport(TestGenKinds.Report, stop == StopReason.Target, stop, request.TargetLinePct,
                baseline.TargetPct, best.Pct, attempts, usage.Snapshot(), best.Diff, best.Bugs);
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
    /// provider → usage counting → the tool loop. The counter sits on the provider so every call the loop makes is
    /// counted; the tool loop has a round cap and turns a refused tool call into text the model can act on.
    /// </summary>
    private IChatClient BuildChatClient(string model, RunUsage usage, TestAgentOptions opts)
    {
        var provider = new OpenTelemetryChatClient(models.CreateChatClient(model));
        var budgeted = new BudgetedChatClient(provider, usage);
        return new FunctionInvokingChatClient(budgeted, loggers)
        {
            MaximumIterationsPerRequest = opts.MaxToolRoundsPerAttempt,
            FunctionInvoker = async (context, ct) =>
            {
                try
                {
                    return await context.Function.InvokeAsync(context.Arguments, ct);
                }
                catch (PathRefusedException ex)
                {
                    return $"Refused: {ex.Message}";
                }
                catch (RunnerUnavailableException)
                {
                    return "The test runner is unavailable right now.";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("tool {Tool} failed ({ErrorType})", context.Function.Name, ex.GetType().Name);
                    return "The tool failed.";
                }
            },
        };
    }

    private static async Task ProgressAsync(TaskUpdater updater, TestGenRequest request, int attempt, string phase, double? pct,
        RunUsage usage, CancellationToken ct) =>
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

using System.Text.Json;
using A2A;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Storage;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Coverage;

/// <summary>How a request to start a run ended, one case per answer the endpoint gives.</summary>
public abstract record StartOutcome
{
    public sealed record Started(RunSummary Run) : StartOutcome;
    public sealed record Invalid(string Field, string Message) : StartOutcome;
    public sealed record ModelRejected(string Message) : StartOutcome;
    public sealed record AlreadyActive(string RunId) : StartOutcome;
    public sealed record NotFound : StartOutcome;
    public sealed record AgentUnavailable(string Reason) : StartOutcome;
}

/// <summary>
/// The api's record of test-generation runs: starting one, applying what its task reports, cancelling it. Every change
/// to a run is also appended as an event, which is what the browser's stream replays and follows.
/// </summary>
public sealed class TestGenRuns(
    IDbContextFactory<MafDbContext> dbFactory,
    CoverageStore store,
    IRepository repository,
    ModelAvailability availability,
    TestAgentClient agent,
    RunActivityStore activity,
    IOptions<TestAgentOptions> agentOptions,
    IOptions<CoverageOptions> coverageOptions,
    TimeProvider time,
    ILogger<TestGenRuns> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Starts a run that raises a file's threshold. The run's row is written first, so the database's one-active-run
    /// index decides a race between two replicas; the threshold is saved only once the agent has accepted the task.
    /// </summary>
    public async Task<StartOutcome> StartAsync(string path, int pct, string model, string userId, CancellationToken ct)
    {
        if (CoveragePaths.Clean(path) != path || await store.CurrentAsync(path, ct) is not { } current)
        {
            return new StartOutcome.NotFound();
        }
        if (pct is < 1 or > 100)
        {
            return new StartOutcome.Invalid("pct", "pct must be a whole percentage from 1 to 100.");
        }
        if (current.Totals.LinePct >= pct)
        {
            return new StartOutcome.Invalid("pct", "The file already meets that threshold; save it without a run.");
        }
        var opts = agentOptions.Value;
        if (opts.Models.FirstOrDefault(m => m.Tag == model) is not { } chosen)
        {
            return new StartOutcome.ModelRejected("That model is not on the allowlist.");
        }
        if (await availability.CheckAsync(chosen.Tag, ct) is { Available: false } unavailable)
        {
            return new StartOutcome.ModelRejected(unavailable.Reason ?? ModelAvailability.NotInPlan);
        }
        var main = coverageOptions.Value.MainBranch;
        if (await repository.ResolveAsync(main, ct) is not { } commit)
        {
            return new StartOutcome.AgentUnavailable($"The {main} branch has no commit.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var row = new TestGenRunRow
        {
            Id = $"r_{Guid.NewGuid():N}",
            Path = path,
            Toolchain = Toolchains.For(path) ?? Toolchains.Dotnet,
            CommitSha = commit,
            TargetPct = pct,
            Model = chosen.Tag,
            State = TestGenRunState.Submitted,
            MaxAttempts = opts.MaxAttempts,
            LastPct = current.Totals.LinePct,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
        };
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.TestGenRuns.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                await using var read = await dbFactory.CreateDbContextAsync(ct);
                var active = await read.TestGenRuns.AsNoTracking()
                    .Where(r => r.Path == path && TestGenRunState.ActiveStates.Contains(r.State)).Select(r => r.Id).FirstOrDefaultAsync(ct);
                return new StartOutcome.AlreadyActive(active ?? "");
            }
        }

        var request = new TestGenRequest(TestGenKinds.Request, row.Id, commit, path, row.Toolchain, pct, opts.MaxAttempts, chosen.Tag,
            new ModelPrice(chosen.InputPerMTok, chosen.OutputPerMTok), new TestGenBudget(opts.Budget.MaxTokens, opts.Budget.MaxCostUsd));
        TaskObservation accepted;
        try
        {
            accepted = await agent.StartAsync(request, ct);
        }
        catch (AgentUnavailableException ex)
        {
            logger.LogWarning("test run {RunId} not started: agent unavailable", row.Id);
            await FinishAsync(row.Id, TestGenRunState.Failed, "agent_unavailable", ct);
            return new StartOutcome.AgentUnavailable(ex.Message);
        }
        if (accepted.State == TaskState.Rejected)
        {
            await FinishAsync(row.Id, TestGenRunState.Failed, "rejected", ct);
            return new StartOutcome.Invalid("request", accepted.StatusText ?? "The test agent rejected the request.");
        }

        // Accepted: the task id and the raised threshold are saved together.
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var saved = await db.TestGenRuns.SingleAsync(r => r.Id == row.Id, ct);
            saved.TaskId = accepted.TaskId;
            saved.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await CoverageEndpoints.SaveThresholdAsync(db, path, pct, userId, ct);
            await tx.CommitAsync(ct);
            row = saved;
        }
        await AppendEventAsync(row, ct);
        return new StartOutcome.Started(RunSummary.Of(row));
    }

    /// <summary>
    /// Applies one observation of the run's task. Returns the run as it now stands. A run that has left the agent's
    /// hands (verifying and after) is not moved by a late or repeated observation.
    /// </summary>
    public async Task<TestGenRunRow?> ApplyAsync(string runId, TaskObservation seen, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.TestGenRuns.SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.State is not (TestGenRunState.Submitted or TestGenRunState.Working))
        {
            return run;
        }
        // What the agent did is kept before what it says about the run, so a stream never shows a state ahead of it.
        await activity.AddAsync(runId, seen.Activity, ct);
        var before = (run.State, run.Attempt, run.LastPct, run.Tokens, run.Phase);
        if (seen.Progress is { } progress)
        {
            run.Phase = progress.Phase;
            run.Attempt = progress.Attempt;
            run.MaxAttempts = progress.MaxAttempts;
            run.LastPct = progress.LastLinePct ?? run.LastPct;
            run.Tokens = progress.Tokens;
            run.CostUsd = progress.CostUsd;
        }
        if (seen.Report is { } report)
        {
            run.ReportJson = JsonSerializer.Serialize(report, TestGenKinds.Json);
            run.Tokens = report.Usage.InputTokens + report.Usage.OutputTokens;
            run.CostUsd = report.Usage.EstimatedCostUsd;
            run.LastPct = report.Final ?? run.LastPct;
            run.Attempt = report.Attempts.Count;
        }
        switch (seen.State)
        {
            case TaskState.Working:
                run.State = TestGenRunState.Working;
                break;
            case TaskState.Completed when run.ReportJson is not null:
                // The agent is done; what it did is not believed until the api has checked it.
                run.State = TestGenRunState.Verifying;
                break;
            case TaskState.Completed:
                run.State = TestGenRunState.Failed;
                run.Reason = "no_report";
                break;
            case TaskState.Failed:
                run.State = TestGenRunState.Failed;
                run.Reason = Code(seen.StatusText);
                break;
            case TaskState.Rejected:
                run.State = TestGenRunState.Failed;
                run.Reason = "rejected";
                break;
            case TaskState.Canceled:
                run.State = TestGenRunState.Canceled;
                break;
        }
        if (before == (run.State, run.Attempt, run.LastPct, run.Tokens, run.Phase) && seen.Report is null)
        {
            return run;
        }
        run.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await AppendEventAsync(run, ct);
        return run;
    }

    /// <summary>Cancels an active run whose task is still with the agent.</summary>
    public async Task<bool> CancelAsync(string runId, CancellationToken ct)
    {
        TestGenRunRow? run;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            run = await db.TestGenRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
        }
        if (run is null || run.State is not (TestGenRunState.Submitted or TestGenRunState.Working))
        {
            return false;
        }
        if (run.TaskId is { } taskId)
        {
            try
            {
                await agent.CancelAsync(runId, taskId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Recorded as canceled here all the same: the person asked for it to stop, and nothing it does from
                // now on will be applied.
                logger.LogWarning("test run {RunId} cancel did not reach the agent ({ErrorType})", runId, ex.GetType().Name);
            }
        }
        await FinishAsync(runId, TestGenRunState.Canceled, null, ct);
        return true;
    }

    /// <summary>Moves a run to a final or later state and records it, unless it is already final.</summary>
    public async Task<TestGenRunRow?> FinishAsync(string runId, string state, string? reason, CancellationToken ct,
        Action<TestGenRunRow>? also = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.TestGenRuns.SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || TestGenRunState.Final.Contains(run.State))
        {
            return run;
        }
        run.State = state;
        run.Reason = reason;
        run.UpdatedAt = time.GetUtcNow().UtcDateTime;
        also?.Invoke(run);
        if (TestGenRunState.Final.Contains(state))
        {
            run.Follower = null;
        }
        await db.SaveChangesAsync(ct);
        await AppendEventAsync(run, ct);
        return run;
    }

    public async Task<TestGenRunRow?> GetAsync(string runId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TestGenRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
    }

    /// <summary>The run's events after <paramref name="afterSeq"/>, in order.</summary>
    public async Task<IReadOnlyList<(int Seq, RunSummary Run)>> EventsAsync(string runId, int afterSeq, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.TestGenRunEvents.AsNoTracking().Where(e => e.RunId == runId && e.Seq > afterSeq)
            .OrderBy(e => e.Seq).ToListAsync(ct);
        return rows.Select(e => (e.Seq, JsonSerializer.Deserialize<RunSummary>(e.Json, Json)!)).ToList();
    }

    /// <summary>Appends the run as it now is. Two replicas appending at once: the unique (run, seq) index decides, and the loser retries.</summary>
    private async Task AppendEventAsync(TestGenRunRow run, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var next = (await db.TestGenRunEvents.Where(e => e.RunId == run.Id).MaxAsync(e => (int?)e.Seq, ct) ?? 0) + 1;
            db.TestGenRunEvents.Add(new TestGenRunEventRow
            {
                RunId = run.Id,
                Seq = next,
                At = time.GetUtcNow().UtcDateTime,
                Json = JsonSerializer.Serialize(RunSummary.Of(run), Json),
            });
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException)
            {
                // Someone else took that number; take the next.
            }
        }
    }

    /// <summary>The agent's failure code from its status text, or a generic one.</summary>
    private static string Code(string? text) =>
        text is TestGenFailure.ModelUnavailable or TestGenFailure.CheckoutFailed or TestGenFailure.RunnerUnavailable
            or TestGenFailure.DiffTooLarge or TestGenFailure.Internal
            ? text
            : "agent_failed";
}

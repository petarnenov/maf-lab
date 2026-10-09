using Maf.Lab.TestGen;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>How the api reaches the agent: where, and as which partner. Never the secret.</summary>
public sealed record TestAgentConnection(string BaseUrl, string ClientId);

public sealed record TestAgentModel(string Tag, string DisplayName);

/// <summary>Runs by what an operator asks about them: in flight, waiting for a person, merged, failed, or ended otherwise.</summary>
public sealed record TestAgentRunCounts(int Running, int Candidates, int Accepted, int Failed, int Other, int Total);

/// <summary>
/// One run as the agents page lists it: no report, no diff, no activity text. <see cref="DurationMs"/> is the run's work
/// time: start to <see cref="FinishedAt"/> once its work ended, start to now while it is running, null when unknown.
/// <see cref="CostUsd"/> is what the run recorded: its model calls priced at the rates sent to the agent at start — the
/// amount its cost cap counts, so far while it runs (show-test-run-cost). <see cref="CostIsEstimate"/>: those rates are
/// the lab's estimate, or the model is no longer on the allowlist.
/// </summary>
public sealed record TestAgentRun(string Id, string Path, string State, string? Reason, int Attempt, int MaxAttempts, double? LastPct,
    int TargetPct, string Model, DateTimeOffset UpdatedAt, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, long? DurationMs,
    long Tokens, double CostUsd, bool CostIsEstimate, RunBudget Budget);

/// <summary>Everything the agents page shows about the test-generation agent, in one answer.</summary>
public sealed record TestAgentOverviewDto(TestAgentStatusDto Status, TestAgentCardDto? Card, TestAgentConnection? Connection,
    TestAgentModel? DefaultModel, int ModelsAllowed, CoverageEndpoints.RunLimitsDto Limits, RunBudget DefaultBudget,
    TestAgentRunCounts Runs, IReadOnlyList<TestAgentRun> Recent);

public static class TestAgentOverview
{
    public const int RecentRuns = 10;

    private static readonly HashSet<string> Failed = [TestGenRunState.Failed, TestGenRunState.VerificationFailed];

    /// <summary>
    /// Runs describe the repository, not a firm, so nothing here is filtered by tenant — as on the Coverage screen. The
    /// defaults are the ones a start request gets, computed by the same helpers the run picker reads.
    /// </summary>
    public static async Task<TestAgentOverviewDto> BuildAsync(TestAgentProbe probe, TestAgentOptions options, DbContext context,
        TimeProvider time, CancellationToken ct)
    {
        var check = probe.CheckAsync(ct);

        var byState = await context.Set<TestGenRunRow>().AsNoTracking()
            .GroupBy(r => r.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var running = TestGenRunState.Running;
        int Count(Func<string, bool> matches) => byState.Where(s => matches(s.State)).Sum(s => s.Count);
        var counts = new TestAgentRunCounts(
            Count(running.Contains),
            Count(s => s == TestGenRunState.Candidate),
            Count(s => s == TestGenRunState.Accepted),
            Count(Failed.Contains),
            Count(s => !running.Contains(s) && !Failed.Contains(s) && s is not (TestGenRunState.Candidate or TestGenRunState.Accepted)),
            byState.Sum(s => s.Count));

        var now = time.GetUtcNow();
        var recent = (await context.Set<TestGenRunRow>().AsNoTracking()
                .OrderByDescending(r => r.UpdatedAt)
                .Take(RecentRuns)
                .ToListAsync(ct))
            .Select(r => Recent(r, now, options.Models))
            .ToList();

        var model = options.Models.FirstOrDefault(m => m.Default) ?? options.Models.FirstOrDefault();
        var limits = new CoverageEndpoints.RunLimitsDto(TestGenRuns.AttemptBounds(options), RunLimits.ToolRoundsPerAttempt,
            RunLimits.TestRunsPerAttempt, TestGenRuns.DeadlineBounds(options), RunLimits.SuspectedBugs);
        var status = await check;

        return new TestAgentOverviewDto(
            status.Status,
            status.Card,
            string.IsNullOrWhiteSpace(options.BaseUrl) ? null : new TestAgentConnection(options.BaseUrl, options.ClientId),
            model is null ? null : new TestAgentModel(model.Tag, model.DisplayName),
            options.Models.Count,
            limits,
            // A run started without a budget is limited only by its attempts and its deadline (TestGenRuns.StartAsync).
            new RunBudget(null, null),
            counts,
            recent);
    }

    /// <summary>A stored time is UTC without a kind; it leaves the api as an instant.</summary>
    private static DateTimeOffset Utc(DateTime at) => new(DateTime.SpecifyKind(at, DateTimeKind.Utc));

    internal static TestAgentRun Recent(TestGenRunRow r, DateTimeOffset now, IReadOnlyList<AgentModelOption> models)
    {
        var started = Utc(r.CreatedAt);
        DateTimeOffset? finished = r.FinishedAt is { } end ? Utc(end) : null;
        var until = finished ?? (TestGenRunState.Running.Contains(r.State) ? now : null);
        long? duration = until is { } u ? Math.Max(0, (long)(u - started).TotalMilliseconds) : null;
        // The recorded cost is never repriced; only whether its price was an estimate is read from today's allowlist.
        var estimate = models.FirstOrDefault(m => m.Tag == r.Model)?.PriceIsEstimate ?? true;
        return new TestAgentRun(r.Id, r.Path, r.State, r.Reason, r.Attempt, r.MaxAttempts, r.LastPct, r.TargetPct, r.Model,
            Utc(r.UpdatedAt), started, finished, duration, r.Tokens, r.CostUsd, estimate, new RunBudget(r.BudgetTokens, r.BudgetCostUsd));
    }
}

using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Coverage;

/// <summary>How an accept or discard ended.</summary>
public abstract record DecisionOutcome
{
    public sealed record Done(TestGenRunRow Run, IReadOnlyList<string> GitHubProblems) : DecisionOutcome;
    public sealed record NotCandidate : DecisionOutcome;
    public sealed record Refused(string Type, string Detail) : DecisionOutcome;
}

/// <summary>
/// A person's decision about a verified run. Accept merges its branch into main and makes its coverage official;
/// discard deletes the branch. The issues its bugs opened are told either way — but GitHub being down never stops the
/// decision itself: what could not be said there is reported back instead.
/// </summary>
public sealed class CandidateDecisions(
    IDbContextFactory<MafDbContext> dbFactory,
    TestGenRuns runs,
    RepoWriter repo,
    CoverageStore store,
    GitHubIssues github,
    IOptions<CoverageOptions> options,
    ILogger<CandidateDecisions> logger)
{
    public async Task<DecisionOutcome> AcceptAsync(string runId, CancellationToken ct)
    {
        if (await runs.GetAsync(runId, ct) is not { State: TestGenRunState.Candidate, Branch: { } branch } run)
        {
            return new DecisionOutcome.NotCandidate();
        }
        // The last moment a stop counts (stop-anything): from here the merge and the record of it are one step, run to
        // the end, so a stop never leaves main half-merged or a merged run still a candidate.
        ct.ThrowIfCancellationRequested();
        var outcome = await repo.MergeAsync(branch, options.Value.MainBranch,
            $"Merge {branch}: tests for {run.Path} (test-agent run {runId})", CancellationToken.None);
        switch (outcome)
        {
            case MergeOutcome.Conflict:
                return new DecisionOutcome.Refused("merge_conflict", "The tests conflict with main; the run stays a candidate.");
            case MergeOutcome.Dirty:
                return new DecisionOutcome.Refused("main_dirty",
                    "Main is checked out with uncommitted changes; commit or stash them, then accept again. Nothing was written.");
            case MergeOutcome.Missing:
                return new DecisionOutcome.Refused("branch_missing", "The run's branch no longer exists.");
        }
        var merge = ((MergeOutcome.Merged)outcome).Commit;
        await store.PromoteAsync(runId, merge, CancellationToken.None);
        var accepted = await runs.FinishAsync(runId, TestGenRunState.Accepted, null, CancellationToken.None, r => r.MergeCommit = merge);
        var problems = await TellIssuesAsync(runId, close: false,
            $"The tests that found this were accepted into main in {merge[..12]}. The test stays skipped until the code is fixed.", ct);
        return new DecisionOutcome.Done(accepted!, problems);
    }

    public async Task<DecisionOutcome> DiscardAsync(string runId, CancellationToken ct)
    {
        if (await runs.GetAsync(runId, ct) is not { State: TestGenRunState.Candidate } run)
        {
            return new DecisionOutcome.NotCandidate();
        }
        // As for accept: a stop counts up to here, and the branch's removal and its record are then one step.
        ct.ThrowIfCancellationRequested();
        if (run.Branch is { } branch)
        {
            await repo.DeleteBranchAsync(branch, CancellationToken.None);
        }
        var discarded = await runs.FinishAsync(runId, TestGenRunState.Discarded, null, CancellationToken.None);
        var problems = await TellIssuesAsync(runId, close: true,
            "The run that found this was discarded, so its test was not kept. Reopen this issue if the bug is real.", ct);
        return new DecisionOutcome.Done(discarded!, problems);
    }

    private async Task<IReadOnlyList<string>> TellIssuesAsync(string runId, bool close, string comment, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var issues = await db.TestGenIssues.AsNoTracking().Where(i => i.RunId == runId && i.Number != null).ToListAsync(ct);
        var problems = new List<string>();
        foreach (var issue in issues)
        {
            try
            {
                await github.CommentAsync(issue.Number!.Value, comment, ct);
                if (close)
                {
                    await github.CloseAsync(issue.Number.Value, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                logger.LogWarning("github update of issue {Number} failed ({ErrorType})", issue.Number, ex.GetType().Name);
                problems.Add($"Issue #{issue.Number} could not be updated on GitHub.");
            }
        }
        return problems;
    }
}

using Maf.Lab.Api.Storage;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;

namespace Maf.Lab.Api.Coverage;

/// <summary>A run as the tree and the file view show it: state and progress, no report.</summary>
public sealed record RunSummary(string Id, string Path, string State, string? Reason, int Attempt, int MaxAttempts,
    double? LastPct, int TargetPct, string Model, long Tokens, double CostUsd, string? Branch, DateTime CreatedAt, DateTime UpdatedAt,
    string? Phase = null, RunBudget? Budget = null)
{
    public bool Active => TestGenRunState.Active.Contains(State);

    public static RunSummary Of(TestGenRunRow r) => new(r.Id, r.Path, r.State, r.Reason, r.Attempt, r.MaxAttempts, r.LastPct,
        r.TargetPct, r.Model, r.Tokens, r.CostUsd, r.Branch, r.CreatedAt, r.UpdatedAt, r.Phase,
        new RunBudget(r.BudgetTokens, r.BudgetCostUsd));
}

/// <summary>A run's caps as the administrator chose them; a null cap is unlimited.</summary>
public sealed record RunBudget(long? MaxTokens, double? MaxCostUsd);

/// <summary>An issue a run's confirmed bug opened.</summary>
public sealed record RunIssue(string TestKey, string Title, int? Number, string? Url);

/// <summary>A run with the agent's report (attempts, suspected bugs, diff) and the issues it opened, for the candidate panel.</summary>
public sealed record RunDetail(RunSummary Run, TestGenReport? Report, IReadOnlyList<RunIssue> Issues)
{
    public static RunDetail Of(TestGenRunRow r, IReadOnlyList<RunIssue>? issues = null) => new(RunSummary.Of(r),
        r.ReportJson is { } json ? System.Text.Json.JsonSerializer.Deserialize<TestGenReport>(json, TestGenKinds.Json) : null,
        issues ?? []);
}

public sealed record CandidateCoverage(string RunId, double Pct, int LinesCovered, int LinesTotal);

/// <summary>A file row of the tree.</summary>
public sealed record TreeFile(
    string Path, string? Toolchain, int LinesTotal, int LinesCovered, int BranchesTotal, int BranchesCovered, double Pct,
    int Threshold, bool ThresholdIsOverride, bool BelowThreshold, string Commit, DateTime MeasuredAt,
    CandidateCoverage? Candidate, RunSummary? Run);

/// <summary>A folder row: coverage over every line of every file under it.</summary>
public sealed record TreeFolder(string Path, int LinesTotal, int LinesCovered, double Pct, int Files, int FilesBelowThreshold);

/// <summary>The whole tree, flat: the browser nests it by path.</summary>
public sealed record CoverageTreeDto(bool HasSnapshot, int DefaultThresholdPct, IReadOnlyList<TreeFile> Files, IReadOnlyList<TreeFolder> Folders);

/// <summary>Builds the tree from the current totals, the thresholds and the runs.</summary>
public static class CoverageTree
{
    public static int EffectiveThreshold(string path, IReadOnlyDictionary<string, int> overrides, int defaultPct) =>
        overrides.TryGetValue(path, out var pct) ? pct : defaultPct;

    public static CoverageTreeDto Build(
        IReadOnlyList<FileTotals> current,
        IReadOnlyDictionary<string, int> overrides,
        int defaultPct,
        IReadOnlyList<FileTotals> candidates,
        IReadOnlyList<RunSummary> runs)
    {
        // A file's run: the active one if there is one, else the most recent, so a finished run's outcome stays visible.
        var runByPath = runs.GroupBy(r => r.Path)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(r => r.Active) ?? g.OrderByDescending(r => r.UpdatedAt).First());
        // Candidates belong to the runs in candidate state; a file has at most one of those at a time.
        var candidateByPath = candidates.ToDictionary(c => c.Path);

        var files = current.Select(f =>
        {
            var threshold = EffectiveThreshold(f.Path, overrides, defaultPct);
            runByPath.TryGetValue(f.Path, out var run);
            CandidateCoverage? candidate = null;
            if (run is { State: TestGenRunState.Candidate } && candidateByPath.TryGetValue(f.Path, out var c))
            {
                candidate = new CandidateCoverage(run.Id, c.LinePct, c.LinesCovered, c.LinesTotal);
            }
            return new TreeFile(f.Path, Toolchains.For(f.Path), f.LinesTotal, f.LinesCovered, f.BranchesTotal, f.BranchesCovered,
                f.LinePct, threshold, overrides.ContainsKey(f.Path), f.LinePct < threshold, f.CommitSha, f.MeasuredAt, candidate, run);
        }).ToList();

        var folders = new Dictionary<string, (int Total, int Covered, int Files, int Below)>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var slash = file.Path.LastIndexOf('/');
            while (slash > 0)
            {
                var folder = file.Path[..slash];
                var agg = folders.GetValueOrDefault(folder);
                folders[folder] = (agg.Total + file.LinesTotal, agg.Covered + file.LinesCovered, agg.Files + 1,
                    agg.Below + (file.BelowThreshold ? 1 : 0));
                slash = folder.LastIndexOf('/');
            }
        }

        return new CoverageTreeDto(
            current.Count > 0,
            defaultPct,
            files,
            folders.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new TreeFolder(f.Key, f.Value.Total, f.Value.Covered, FileCoverage.Pct(f.Value.Covered, f.Value.Total),
                    f.Value.Files, f.Value.Below))
                .ToList());
    }
}

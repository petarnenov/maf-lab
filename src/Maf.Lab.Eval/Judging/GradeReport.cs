using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Html;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;

namespace Maf.Lab.Eval.Judging;

/// <summary>
/// Where graded runs are kept and how they are shown (adopt-meai-evaluation, design D1/D5): each case's result is
/// stored under <c>evals/reports/meai/</c> (git-ignored with the rest of the reports), one execution per run, and the
/// run's HTML report is rendered beside its JSON and Markdown ones from the latest executions, so it shows how each
/// case's scores moved. No response cache: there is no chat call to cache, and Jev answers identical input identically.
/// </summary>
public static class GradeReport
{
    /// <summary>Executions the HTML report draws its trends from.</summary>
    public const int TrendExecutions = 10;

    public static string StorePath(string root) => Path.Combine(root, "reports", "meai");

    public static ReportingConfiguration Configure(string root, string runId, IEvaluator evaluator) =>
        DiskBasedReportingConfiguration.Create(StorePath(root), [evaluator], chatConfiguration: null, enableResponseCaching: false,
            executionName: runId);

    /// <summary>Renders <c>reports/&lt;runId&gt;.html</c> from this run and the ones before it; returns its path.</summary>
    public static async Task<string> WriteHtmlAsync(string root, string runId, string scenarioPrefix, CancellationToken ct)
    {
        var store = new DiskBasedResultStore(StorePath(root));
        var results = new List<ScenarioRunResult>();
        await foreach (var execution in store.GetLatestExecutionNamesAsync(TrendExecutions, ct))
        {
            await foreach (var result in store.ReadResultsAsync(execution, cancellationToken: ct))
            {
                // One store holds both suites; a report shows its own suite's cases only.
                if (result.ScenarioName.StartsWith(scenarioPrefix, StringComparison.Ordinal))
                {
                    results.Add(result);
                }
            }
        }
        var path = Path.Combine(root, "reports", $"{runId}.html");
        await new HtmlReportWriter(path).WriteReportAsync(results, ct);
        return path;
    }
}

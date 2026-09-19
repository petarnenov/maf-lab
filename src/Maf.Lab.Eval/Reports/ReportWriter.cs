using System.Globalization;
using System.Text;
using System.Text.Json;
using Maf.Lab.Domain.Evals;

namespace Maf.Lab.Eval.Reports;

/// <summary>Writes {root}/reports/{runId}.json (read by the web /evals page) and a Markdown summary next to it.</summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<string> WriteAsync(string root, EvalReport report, CancellationToken ct)
    {
        var dir = Path.Combine(root, "reports");
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, $"{report.RunId}.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, Json), ct);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{report.RunId}.md"), Markdown(report), ct);
        return jsonPath;
    }

    public static string Markdown(EvalReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Eval report: {report.Suite} — {(report.Passed ? "PASSED" : "FAILED")}");
        sb.AppendLine();
        sb.AppendLine($"Run `{report.RunId}`, {report.StartedAt:u} → {report.FinishedAt:u}");
        sb.AppendLine();
        foreach (var (k, v) in report.Settings)
        {
            sb.AppendLine($"- {k}: `{v}`");
        }
        sb.AppendLine();
        var metricNames = report.Variants.SelectMany(v => v.Metrics.Keys).Distinct().ToList();
        sb.AppendLine($"| variant | cases | {string.Join(" | ", metricNames)} | result |");
        sb.AppendLine($"|---|---|{string.Concat(metricNames.Select(_ => "---|"))}---|");
        foreach (var v in report.Variants)
        {
            var cells = metricNames.Select(m => v.Metrics.TryGetValue(m, out var x)
                ? x.ToString("0.###", CultureInfo.InvariantCulture) + (v.Thresholds.TryGetValue(m, out var t) ? $" (≥{t.ToString(CultureInfo.InvariantCulture)})" : "")
                : "");
            sb.AppendLine($"| {v.Name} | {v.Cases} | {string.Join(" | ", cells)} | {(v.Thresholds.Count == 0 ? "info" : v.Passed ? "pass" : "FAIL")} |");
        }
        foreach (var v in report.Variants.Where(v => v.Failures.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine($"## {v.Name}: {v.Failures.Count} case(s) below target");
            foreach (var f in v.Failures.Take(30))
            {
                sb.AppendLine($"- `{f.CaseId}` — {f.Reason}");
            }
        }
        return sb.ToString();
    }
}

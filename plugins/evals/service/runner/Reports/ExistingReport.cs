using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Evals;

namespace Maf.Lab.Eval.Reports;

/// <summary>Accept a completed report through the existing baseline rule, without starting an evaluation host.</summary>
public static class ExistingReport
{
    public static async Task<string> AcceptAsync(string root, string runId, CancellationToken ct)
    {
        if (!Regex.IsMatch(runId, @"\A[A-Za-z0-9][A-Za-z0-9_-]*\z"))
            throw new InvalidDataException("REPORT must be a run ID, not a path.");
        await using var stream = File.OpenRead(Path.Combine(root, "reports", runId + ".json"));
        var report = await JsonSerializer.DeserializeAsync<EvalReport>(stream, JsonSerializerOptions.Web, ct)
            ?? throw new InvalidDataException("The report is empty.");
        if (report.RunId != runId || string.IsNullOrWhiteSpace(report.Suite) || !report.Passed)
            throw new InvalidDataException("Only a matching completed, passed report can be accepted.");
        if (Program.ComparisonSuites.Contains(report.Suite))
            throw new InvalidDataException("Comparison reports cannot be accepted into the baseline.");
        if (report.Variants.Count == 0 || report.Variants.Any(v => !v.Passed
            || v.Thresholds.Any(t => !v.Metrics.TryGetValue(t.Key, out var value) || !double.IsFinite(value) || value < t.Value)))
            throw new InvalidDataException("The report has a variant below its thresholds.");
        var current = await BaselineStore.ReadAsync(root, ct);
        var accepted = BaselineStore.Accept(current, report.Suite, report.Variants, report.RunId, report.FinishedAt)
            ?? throw new InvalidDataException("The report cannot be accepted.");
        return await BaselineStore.WriteAsync(root, accepted, ct);
    }
}

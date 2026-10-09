using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maf.Lab.A2AProbe;

/// <summary>
/// One line of `evals/a2a-conformance.jsonl`: what an outside client must be able to do, and how to tell whether
/// it did. The probe owns no list of its own — a scenario exists because a row names it.
/// </summary>
/// <param name="Scenario">The runner to use. A name no runner answers to is a failure, not a skip.</param>
public sealed record ConformanceRow(
    string Id,
    string Scenario,
    string What,
    string? Message,
    string? Reply,
    JsonElement Expect);

/// <summary>What one row did.</summary>
public sealed record ConformanceOutcome(string Id, string Scenario, bool Passed, string Detail, string? Reason = null);

/// <summary>
/// The report shape the eval harness writes, rebuilt here rather than referenced.
///
/// The probe deliberately has no project reference to `src/` — that independence is the whole claim it exists to
/// support — so it cannot use <c>Maf.Lab.Domain.Evals.EvalReport</c>. These records serialize to the same JSON,
/// and a test in the solution holds the two shapes together.
/// </summary>
public sealed record ConformanceReport(
    string RunId,
    string Suite,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<ConformanceVariant> Variants,
    bool Passed);

public sealed record ConformanceVariant(
    string Name,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyDictionary<string, double> Thresholds,
    bool Passed,
    int Cases,
    IReadOnlyList<ConformanceFailure> Failures);

public sealed record ConformanceFailure(string CaseId, string Reason);

/// <summary>Reads the dataset and writes the report beside every other suite's.</summary>
public static class Conformance
{
    public const string Suite = "a2a-conformance";
    public const string Variant = "partner";

    private static readonly JsonSerializerOptions Read = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly JsonSerializerOptions Write = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static IReadOnlyList<ConformanceRow> Load(string path)
    {
        var rows = new List<ConformanceRow>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Trim() is { Length: > 0 } text && !text.StartsWith('#'))
            {
                rows.Add(JsonSerializer.Deserialize<ConformanceRow>(text, Read)
                    ?? throw new InvalidOperationException($"Unreadable row in {path}: {text}"));
            }
        }
        if (rows.Count == 0)
        {
            throw new InvalidOperationException($"{path} names no scenarios.");
        }
        return rows;
    }

    public static ConformanceReport Build(IReadOnlyList<ConformanceOutcome> outcomes,
        IReadOnlyDictionary<string, string> settings, DateTimeOffset startedAt, DateTimeOffset finishedAt)
    {
        var passRate = outcomes.Count == 0 ? 0d : (double)outcomes.Count(o => o.Passed) / outcomes.Count;
        var failures = outcomes.Where(o => !o.Passed)
            .Select(o => new ConformanceFailure(o.Id, o.Reason ?? o.Detail))
            .ToList();

        // Conformance is not a metric that drifts: a scenario the specification requires either works or does not.
        var variant = new ConformanceVariant(Variant,
            new Dictionary<string, double> { ["passRate"] = passRate },
            new Dictionary<string, double> { ["passRate"] = 1 },
            failures.Count == 0, outcomes.Count, failures);

        return new ConformanceReport(
            $"{startedAt:yyyyMMdd-HHmmss}-{Suite}", Suite, startedAt, finishedAt, settings, [variant],
            failures.Count == 0);
    }

    public static async Task<string> WriteAsync(string root, ConformanceReport report, CancellationToken ct)
    {
        var dir = Path.Combine(root, "reports");
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, $"{report.RunId}.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, Write), ct);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{report.RunId}.md"), Markdown(report), ct);
        return jsonPath;
    }

    /// <summary>The same summary the harness writes, so a conformance failure reads like any other failing eval.</summary>
    public static string Markdown(ConformanceReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Eval report: {report.Suite} — {(report.Passed ? "PASSED" : "FAILED")}");
        sb.AppendLine();
        sb.AppendLine($"Run `{report.RunId}`, {report.StartedAt:u} → {report.FinishedAt:u}");
        sb.AppendLine();
        foreach (var (key, value) in report.Settings)
        {
            sb.AppendLine($"- {key}: `{value}`");
        }
        sb.AppendLine();
        sb.AppendLine("| variant | cases | passRate | result |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var variant in report.Variants)
        {
            var rate = variant.Metrics.TryGetValue("passRate", out var value) ? value : 0;
            sb.AppendLine($"| {variant.Name} | {variant.Cases} | {rate:0.###} (≥1) | {(variant.Passed ? "pass" : "FAIL")} |");
        }
        foreach (var variant in report.Variants.Where(v => v.Failures.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine($"## {variant.Name}: {variant.Failures.Count} scenario(s) failed");
            foreach (var failure in variant.Failures)
            {
                sb.AppendLine($"- `{failure.CaseId}` — {failure.Reason}");
            }
        }
        return sb.ToString();
    }
}

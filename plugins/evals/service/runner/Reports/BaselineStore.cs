using System.Text.Json;
using Maf.Lab.Domain.Evals;

namespace Maf.Lab.Eval.Reports;

/// <summary>
/// Reads and writes {root}/baseline.json — the metrics this repository has accepted. It is committed, unlike
/// reports/, so the gate works on a fresh checkout and a change to it shows up in review.
/// </summary>
public static class BaselineStore
{
    // Indented and in a fixed key order: a moved number must be the only thing a diff shows.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string PathFor(string root) => Path.Combine(root, "baseline.json");

    /// <summary>A missing file is not an error: it means nothing has been accepted yet.</summary>
    public static async Task<EvalBaseline> ReadAsync(string root, CancellationToken ct)
    {
        var path = PathFor(root);
        if (!File.Exists(path))
        {
            return EvalBaseline.Empty;
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<EvalBaseline>(stream, Json, ct) ?? EvalBaseline.Empty;
    }

    public static async Task<string> WriteAsync(string root, EvalBaseline baseline, CancellationToken ct)
    {
        var path = PathFor(root);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Ordered(baseline), Json) + "\n", ct);
        return path;
    }

    /// <summary>Replaces one suite's entry, keeping every other suite as it was.</summary>
    public static EvalBaseline With(EvalBaseline baseline, string suite, SuiteBaseline accepted)
    {
        var suites = new Dictionary<string, SuiteBaseline>(baseline.Suites, StringComparer.Ordinal)
        {
            [suite] = accepted,
        };
        return new EvalBaseline(suites);
    }

    /// <summary>
    /// The updated baseline, or null when this run must not be accepted. Accepting a run that is below its
    /// thresholds would bless exactly what the floors rejected.
    /// </summary>
    public static EvalBaseline? Accept(EvalBaseline current, string suite, IReadOnlyList<EvalVariantResult> variants,
        string runId, DateTimeOffset at) =>
        variants.Count > 0 && variants.All(v => v.Passed)
            ? With(current, suite, FromVariants(variants, runId, at))
            : null;

    public static SuiteBaseline FromVariants(IReadOnlyList<EvalVariantResult> variants, string runId, DateTimeOffset at) =>
        new(variants.ToDictionary(
                v => v.Name,
                v => (IReadOnlyDictionary<string, double>)new SortedDictionary<string, double>(
                    v.Metrics.ToDictionary(m => m.Key, m => m.Value), StringComparer.Ordinal),
                StringComparer.Ordinal),
            runId, at);

    private static EvalBaseline Ordered(EvalBaseline baseline) =>
        new(new SortedDictionary<string, SuiteBaseline>(
            baseline.Suites.ToDictionary(
                s => s.Key,
                s => s.Value with
                {
                    Metrics = new SortedDictionary<string, IReadOnlyDictionary<string, double>>(
                        s.Value.Metrics.ToDictionary(
                            v => v.Key,
                            v => (IReadOnlyDictionary<string, double>)new SortedDictionary<string, double>(
                                v.Value.ToDictionary(m => m.Key, m => m.Value), StringComparer.Ordinal),
                            StringComparer.Ordinal),
                        StringComparer.Ordinal),
                }),
            StringComparer.Ordinal));
}

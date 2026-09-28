using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Retrieval.Jev;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Eval.Suites;

/// <summary>
/// Jev's domain verdict on its own (add-portfolio-domain): for each question, which domains are in scope — billing,
/// portfolio, both (the question crosses the boundary) or none. No answering model and no tool runs; it measures the
/// one decision that picks which servers a turn searches, and calibrates the scope floor.
/// </summary>
public sealed class DomainSuite(EvalAgentHost host)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        if (!host.Services.GetRequiredService<JevCredential>().IsConfigured)
        {
            throw new InvalidOperationException($"The domain suite needs {JevCredential.EnvironmentVariable} in the environment.");
        }
        var classifier = host.Services.GetRequiredService<IIntentClassifier>();
        var cases = ctx.Take(DatasetLoader.Domain(ctx.DatasetRoot)).ToList();
        var outcomes = new List<(DomainCase Case, string Actual)>();
        var answered = new List<(DomainCase Case, IReadOnlyDictionary<string, double>? Probabilities, bool Forcing)>();
        var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JevOptions>>().Value;
        var failures = new List<EvalCaseFailure>();
        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var decision = await classifier.ClassifyAsync(c.Question, ct);
            var actual = Label(decision.Domains);
            outcomes.Add((c, actual));
            answered.Add((c, decision.Domains?.Probabilities, decision.Choice is "procedural" or "mixed"));
            if (actual != c.Expected)
            {
                var p = decision.Domains?.Probabilities;
                failures.Add(new EvalCaseFailure(c.Id,
                    $"expected {c.Expected}, got {actual} — billing={p?.GetValueOrDefault(Domains.Billing):0.00} "
                    + $"portfolio={p?.GetValueOrDefault(Domains.Portfolio):0.00} intent={decision.Intent} reason={decision.Reason ?? "-"}"));
            }
            ctx.Progress($"domain {i + 1}/{cases.Count} {c.Id}: {actual}{(actual == c.Expected ? "" : $" (expected {c.Expected})")}");
        }
        return
        [
            SuiteContext.Variant("jev", Metrics.Domain(outcomes.Select(o => (o.Case.Expected, o.Actual, o.Case.Language, o.Case.Split))),
                ctx.ThresholdsFor("domain"), cases.Count, failures),
            // The same answers read at other scope floors: one request per question calibrates the floor, where a run
            // per candidate would pay for every question again.
            SuiteContext.Variant("floor-sweep", Sweep(answered, options.MinInDomain), new Dictionary<string, double>(), cases.Count, []),
        ];
    }

    internal static readonly double[] Floors = [0.3, 0.4, 0.5, 0.6, 0.7, 0.8];

    /// <summary>Accuracy and crossing recall/precision at each candidate scope floor, from the probabilities Jev gave.</summary>
    internal static Dictionary<string, double> Sweep(
        IReadOnlyList<(DomainCase Case, IReadOnlyDictionary<string, double>? Probabilities, bool Forcing)> answered, double gateFloor)
    {
        var metrics = new Dictionary<string, double>();
        foreach (var floor in Floors)
        {
            var labelled = answered.Select(a => (a.Case.Expected,
                Actual: Label(a.Probabilities is null ? null : DomainVerdict.From(a.Probabilities, floor, gateFloor)),
                a.Case.Language, a.Case.Split));
            var m = Metrics.Domain(labelled);
            var key = floor.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            metrics[$"accuracy@{key}"] = m["accuracy"];
            metrics[$"crossingRecall@{key}"] = m["crossingRecall"];
            metrics[$"crossingPrecision@{key}"] = m["crossingPrecision"];
            metrics[$"noneAccuracy@{key}"] = m["noneAccuracy"];
        }
        return metrics;
    }

    /// <summary>The verdict in the dataset's words: one domain, both, or none.</summary>
    internal static string Label(DomainVerdict? verdict) => verdict?.InScope switch
    {
        null or { Count: 0 } => "none",
        { Count: > 1 } => "both",
        [var only] => only,
    };
}
